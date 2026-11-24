using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Drs.Unreal.Common;
using Drs.Unreal.Common.Engines;
using Drs.Unreal.Common.Launch;
using Drs.Unreal.Common.Maps;
using Drs.Unreal.Common.Projects;
using UETestTool.Services;

namespace UETestTool.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IUserDialogs _dialogs;
    private readonly DispatcherTimer _saveTimer;

    private ProjectConfigFile? _configFile;
    private ProjectUserState _userState = new();
    private string? _userStatePath;

    /// <summary>True while the UI is being populated from saved state; selection changes then don't write state back.</summary>
    private bool _loading;

    /// <summary>Guards against a slow map scan for a previous project overwriting the current one.</summary>
    private int _projectLoadVersion;

    public MainViewModel(AppSettings settings, IUserDialogs dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => FlushUserState();

        _processes.Changed += () =>
        {
            OnPropertyChanged(nameof(StopText));
            OnPropertyChanged(nameof(StopToolTip));
            StopCommand.NotifyCanExecuteChanged();
        };

        LaunchItemsView = CollectionViewSource.GetDefaultView(LaunchItems);
        LaunchItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ILaunchItem.Group)));
    }

    public ObservableCollection<ProjectItem> Projects { get; } = [];
    public ObservableCollection<EngineInstall> Engines { get; } = [];
    public ObservableCollection<MapInfo> Maps { get; } = [];
    public ObservableCollection<ConfigItem> Configs { get; } = [];
    public ObservableCollection<SessionItem> Sessions { get; } = [];

    /// <summary>Configurations then sessions: the rows of the list.</summary>
    public ObservableCollection<ILaunchItem> LaunchItems { get; } = [];

    /// <summary><see cref="LaunchItems"/> grouped under their headings.</summary>
    public ICollectionView LaunchItemsView { get; }

    [ObservableProperty] private ProjectItem? _selectedProject;
    [ObservableProperty] private EngineInstall? _selectedEngine;
    [ObservableProperty] private MapInfo? _selectedMap;

    /// <summary>The selected row: a configuration or a session.</summary>
    [ObservableProperty] private ILaunchItem? _selectedItem;

    public ConfigItem? SelectedConfig => SelectedItem as ConfigItem;
    public SessionItem? SelectedSession => SelectedItem as SessionItem;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusIsError;

    /// <summary>Shown when the selected engine isn't the one the project is associated with.</summary>
    [ObservableProperty] private string? _engineWarning;
    [ObservableProperty] private EngineInstall? _suggestedEngine;

    /// <summary>Problems in the project's UETestTool.ini (unknown templates etc.).</summary>
    [ObservableProperty] private string? _configWarnings;

    /// <summary>The selected row's command line(s), shown on the Launch button's tooltip.</summary>
    [ObservableProperty] private string _commandPreview = "";

    public bool IsMapEnabled => SelectedSession is not null || (SelectedConfig?.Config.Rule.UsesMap ?? true);

    public string MapHint => SelectedConfig?.Config.Rule is { UsesMap: false } rule
        ? (string.IsNullOrEmpty(rule.Connect) ? "Not used by this configuration" : $"Not used: connects to {rule.Connect}")
        : (Maps.Count == 0 && SelectedProject is not null ? "No maps found: the project's default map will be used" : "");

    public string SharedConfigPath => SelectedProject is null ? "" : ProjectFiles.SharedConfig(SelectedProject.Directory);

    // ---- Startup / refresh ----------------------------------------------------------------------------

    /// <summary>Shows cached projects immediately, then discovers in the background.</summary>
    public async Task InitializeAsync()
    {
        _loading = true;
        foreach (var entry in _settings.ProjectCache.Projects.OrderBy(p => System.IO.Path.GetFileNameWithoutExtension(p.Path), StringComparer.OrdinalIgnoreCase))
            Projects.Add(new ProjectItem(new UProjectFile(entry.Path, null), ProjectAvailability.Available));
        _loading = false;

        await RefreshAsync(_settings.LastProject);
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync(SelectedProject?.Path ?? _settings.LastProject);

    /// <summary>Set when a refresh is asked for while one is running (e.g. search folders changed); it runs again afterwards.</summary>
    private bool _refreshPending;

    private async Task RefreshAsync(string? selectPath)
    {
        if (IsBusy)
        {
            _refreshPending = true;
            return;
        }
        IsBusy = true;
        SetStatus("Searching for engines and projects…");
        FlushUserState();
        _logVersions.Clear();

        var cachedPaths = _settings.ProjectCache.Projects.Select(p => p.Path).ToList();
        var searchFolders = _settings.SearchFolders.ToList();

        var result = await Task.Run(() =>
        {
            var engines = EngineDiscovery.Discover();
            var discovered = ProjectDiscovery.Discover(searchFolders, engines.Select(e => e.RootDir));
            var allPaths = cachedPaths.Concat(discovered.Select(d => d.UProjectPath)).Distinct(PathUtil.Comparer).ToList();
            var availability = allPaths.ToDictionary(p => p, ProjectCache.CheckAvailability, PathUtil.Comparer);
            var files = allPaths
                .Where(p => availability[p] == ProjectAvailability.Available)
                .ToDictionary(p => p, UProjectFile.Read, PathUtil.Comparer);
            return (engines, discovered, availability, files);
        });

        var cache = _settings.ProjectCache;
        var added = cache.Merge(result.discovered);
        var countBefore = cache.Projects.Count;
        var outsideFolders = cache.Prune(searchFolders);
        var availabilityOfKept = cache.Validate(p => result.availability.GetValueOrDefault(p, ProjectAvailability.Unavailable));
        var removed = countBefore - cache.Projects.Count - outsideFolders;

        _loading = true;
        try
        {
            var previousEngine = SelectedEngine?.Identifier ?? _settings.LastEngine;
            Engines.Clear();
            foreach (var e in result.engines) Engines.Add(e);
            SelectedEngine = Engines.FirstOrDefault(e => e.Identifier == previousEngine) ?? Engines.FirstOrDefault();

            Projects.Clear();
            foreach (var entry in cache.Projects)
            {
                var available = availabilityOfKept[entry.Path];
                var file = result.files.GetValueOrDefault(entry.Path) ?? new UProjectFile(entry.Path, null);
                Projects.Add(new ProjectItem(file, available));
            }
            SortProjects();
        }
        finally
        {
            _loading = false;
        }

        _settings.Save();

        var target = Projects.FirstOrDefault(p => p.IsAvailable && PathUtil.Comparer.Equals(p.Path, selectPath ?? ""))
                     ?? Projects.FirstOrDefault(p => p.IsAvailable);
        // Always (re)load the project: a refresh should also pick up hand edits to its .ini and new maps.
        if (ReferenceEquals(target, SelectedProject)) await LoadProjectAsync(target);
        else SelectedProject = target;   // triggers LoadProjectAsync

        IsBusy = false;
        var summary = $"{Engines.Count} engine(s), {Projects.Count} project(s)";
        if (added > 0) summary += $", {added} new";
        if (removed > 0) summary += $", {removed} removed (no longer on disk)";
        if (outsideFolders > 0) summary += $", {outsideFolders} removed (not in a search folder)";
        if (Engines.Count == 0) SetStatus("No Unreal Engine installations found.", isError: true);
        else if (_settings.SearchFolders.Count == 0)
            SetStatus("No search folders: click Folders… to choose where to look for projects.", isError: true);
        else SetStatus(summary + ".");

        if (_refreshPending)
        {
            _refreshPending = false;
            await RefreshAsync(SelectedProject?.Path ?? _settings.LastProject);
        }
    }

    [RelayCommand]
    private async Task EditSearchFolders()
    {
        var edited = _dialogs.EditSearchFolders(_settings.SearchFolders);
        if (edited is null || edited.SequenceEqual(_settings.SearchFolders, PathUtil.Comparer)) return;

        _settings.SearchFolders = edited.Distinct(PathUtil.Comparer).ToList();
        _settings.Save();
        await RefreshAsync(SelectedProject?.Path ?? _settings.LastProject);
    }

    private void SortProjects()
    {
        var sorted = Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var current = Projects.IndexOf(sorted[i]);
            if (current != i) Projects.Move(current, i);
        }
    }

    [RelayCommand]
    private async Task Browse()
    {
        var path = _dialogs.PickProjectFile(SelectedProject?.Directory);
        if (path is null) return;

        var entry = _settings.ProjectCache.AddBrowsed(path);
        _settings.Save();
        var existing = Projects.FirstOrDefault(p => PathUtil.Comparer.Equals(p.Path, entry.Path));
        if (existing is null)
        {
            existing = new ProjectItem(await Task.Run(() => UProjectFile.Read(entry.Path)), ProjectAvailability.Available);
            Projects.Add(existing);
            SortProjects();
        }
        SelectedProject = existing;
    }

    [RelayCommand]
    private void RemoveProject(ProjectItem? project)
    {
        if (project is null) return;
        if (!_dialogs.Confirm("Remove project",
                $"Remove {project.Name} from the list?\n\n{project.Path}\n\nThe project itself is not touched. Use Browse… to add it back.")) return;

        _settings.ProjectCache.Remove(project.Path);
        _settings.Save();
        var wasSelected = ReferenceEquals(project, SelectedProject);
        Projects.Remove(project);
        if (wasSelected) SelectedProject = Projects.FirstOrDefault(p => p.IsAvailable);
    }

    // ---- Project selection ------------------------------------------------------------------------------

    partial void OnSelectedProjectChanged(ProjectItem? oldValue, ProjectItem? newValue)
    {
        if (_loading) return;   // list being rebuilt by Refresh; it selects the right project afterwards
        if (oldValue is not null) FlushUserState();
        _ = LoadProjectAsync(newValue);
    }

    private async Task LoadProjectAsync(ProjectItem? project)
    {
        var version = ++_projectLoadVersion;
        _loading = true;
        try
        {
            Maps.Clear();
            Configs.Clear();
            Sessions.Clear();
            LaunchItems.Clear();
            _configFile = null;
            _userStatePath = null;
            _userState = new ProjectUserState();
            ConfigWarnings = null;

            if (project is null || !project.IsAvailable)
            {
                UpdateEngineWarning();
                UpdateCommandPreview();
                return;
            }

            _settings.LastProject = project.Path;
            _settings.Save();

            _userStatePath = ProjectFiles.UserState(project.Directory);
            _userState = ProjectUserState.Load(_userStatePath);
            _configFile = ProjectConfigFile.Load(ProjectFiles.SharedConfig(project.Directory));
            ReloadConfigs(_userState.LastConfig, _userState.LastSession);

            // Engine: the one last used with this project, else the project's own association (or an installed engine
            // of the same release), else the last one used anywhere.
            SelectedEngine = Engines.FirstOrDefault(e => e.Identifier == _userState.LastEngine)
                             ?? ProjectEngine.Preferred(IdentifyEngine(project), Engines)
                             ?? Engines.FirstOrDefault(e => e.Identifier == _settings.LastEngine)
                             ?? Engines.FirstOrDefault();
            UpdateEngineWarning();
        }
        finally
        {
            _loading = false;
            OnPropertyChanged(nameof(SharedConfigPath));
        }

        // Maps are scanned in the background; the user can already change other settings meanwhile.
        var maps = await Task.Run(() => MapDiscovery.Discover(project.Directory));
        if (version != _projectLoadVersion) return;   // the user picked another project meanwhile

        _loading = true;
        try
        {
            foreach (var m in maps) Maps.Add(m);
            SelectedMap = Maps.FirstOrDefault(m => m.PackagePath == _userState.LastMap)
                          ?? Maps.FirstOrDefault(m => m.PluginName is null)
                          ?? Maps.FirstOrDefault();
        }
        finally
        {
            _loading = false;
            OnPropertyChanged(nameof(MapHint));
            UpdateCommandPreview();
        }
    }

    /// <summary>Re-reads the configurations and sessions, then selects the named session, else the named configuration.</summary>
    private void ReloadConfigs(string? select, string? selectSession = null)
    {
        if (_configFile is null) return;
        var wasLoading = _loading;
        _loading = true;
        try
        {
            Configs.Clear();
            Sessions.Clear();
            LaunchItems.Clear();
            var configs = _configFile.GetConfigs();
            foreach (var config in configs)
                Configs.Add(new ConfigItem(config, _userState.GetCustomArgs(config.Name), _configFile.Defines(config.Name), OnCustomArgsChanged));
            foreach (var session in _configFile.GetSessions(configs))
                Sessions.Add(new SessionItem(session, _configFile.DefinesSession(session.Name), session.Resolve(configs).Error));
            foreach (var item in Configs) LaunchItems.Add(item);
            foreach (var item in Sessions) LaunchItems.Add(item);
            ConfigWarnings = _configFile.Warnings.Count == 0 ? null : string.Join("\n", _configFile.Warnings);
            SelectedItem = (ILaunchItem?)Sessions.FirstOrDefault(s => selectSession is not null && string.Equals(s.Name, selectSession, StringComparison.OrdinalIgnoreCase))
                           ?? Configs.FirstOrDefault(c => string.Equals(c.Name, select, StringComparison.OrdinalIgnoreCase))
                           ?? Configs.FirstOrDefault();
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    // ---- Other selections ---------------------------------------------------------------------------

    partial void OnSelectedEngineChanged(EngineInstall? value)
    {
        UpdateEngineWarning();
        UpdateCommandPreview();
        if (_loading || value is null) return;
        _settings.LastEngine = value.Identifier;
        _settings.Save();
        _userState.LastEngine = value.Identifier;
        SaveUserStateSoon();
    }

    partial void OnSelectedMapChanged(MapInfo? value)
    {
        UpdateCommandPreview();
        if (_loading || value is null) return;
        _userState.LastMap = value.PackagePath;
        SaveUserStateSoon();
    }

    partial void OnSelectedItemChanged(ILaunchItem? value)
    {
        OnPropertyChanged(nameof(SelectedConfig));
        OnPropertyChanged(nameof(SelectedSession));
        OnPropertyChanged(nameof(IsMapEnabled));
        OnPropertyChanged(nameof(MapHint));
        UpdateCommandPreview();
        if (_loading || value is null) return;
        RememberSelection();
        SaveUserStateSoon();
    }

    private void RememberSelection()
    {
        if (SelectedConfig is { } config)
        {
            _userState.LastConfig = config.Name;
            _userState.LastSession = null;
        }
        else if (SelectedSession is { } session) _userState.LastSession = session.Name;
    }

    private void OnCustomArgsChanged(ConfigItem item)
    {
        if (ReferenceEquals(item, SelectedConfig) || SelectedSession is not null) UpdateCommandPreview();
        if (_loading) return;
        _userState.CustomArgs[item.Name] = item.CustomArgs;
        SaveUserStateSoon();
    }

    private void UpdateEngineWarning()
    {
        SuggestedEngine = null;
        EngineWarning = null;
        if (SelectedProject is not { IsAvailable: true } project) return;

        var check = ProjectEngine.Check(project.Name, IdentifyEngine(project), SelectedEngine, Engines);
        EngineWarning = check.Warning;
        SuggestedEngine = check.Suggested;
    }

    /// <summary>Engine versions from project logs, read once per project folder (cleared on Refresh).</summary>
    private readonly Dictionary<string, EngineVersion?> _logVersions = new(PathUtil.Comparer);

    private ProjectEngineInfo IdentifyEngine(ProjectItem project) =>
        ProjectEngine.Identify(project.File.EngineAssociation, project.Path, Engines, dir =>
        {
            if (!_logVersions.TryGetValue(dir, out var v)) _logVersions[dir] = v = ProjectEngine.ReadVersionFromLog(dir);
            return v;
        });

    [RelayCommand]
    private void SwitchEngine()
    {
        if (SuggestedEngine is not null) SelectedEngine = SuggestedEngine;
    }

    // ---- New / edit / delete configurations ----------------------------------------------------------

    [RelayCommand]
    private void NewConfig() => EditConfigCore(null);

    [RelayCommand]
    private void EditConfig(ConfigItem? item)
    {
        if (item is not null) EditConfigCore(item);
    }

    /// <summary>
    /// Opens the configuration editor. When editing, the configuration is its own template, so it keeps its launch rule;
    /// saving under the same name updates it (or, for a built-in, creates a project override).
    /// </summary>
    private void EditConfigCore(ConfigItem? item)
    {
        if (_configFile is null || SelectedProject is null)
        {
            SetStatus("Select a project first.", isError: true);
            return;
        }
        var edit = _dialogs.EditConfig(item?.Config, Configs.Select(c => c.Config).ToList(), SelectedConfig?.Config);
        if (edit is null) return;

        var existing = Configs.FirstOrDefault(c => string.Equals(c.Name, edit.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !existing.IsProjectDefined)
        {
            if (!_dialogs.Confirm("Override built-in",
                    $"'{existing.Name}' is a built-in configuration.\n\nSaving will override it for everyone who uses this project's " +
                    $"Config/UETestTool.ini. Deleting the override later restores the built-in.\n\nContinue?")) return;
        }
        else if (existing is not null && !ReferenceEquals(existing, item))
        {
            if (!_dialogs.Confirm("Replace configuration", $"Replace the existing configuration '{existing.Name}'?")) return;
        }

        var name = existing?.Name ?? edit.Name;
        try
        {
            _configFile.Save(name, edit.Template, edit.Args);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't write {_configFile.Path}: {ex.Message}", isError: true);
            return;
        }

        ReloadConfigs(name);
        RememberSelection();
        SaveUserStateSoon();
        SetStatus($"Saved '{name}' to Config/UETestTool.ini.");
    }

    [RelayCommand]
    private void DeleteConfig(ConfigItem? item)
    {
        if (item is null || _configFile is null || !item.IsProjectDefined) return;
        var isOverride = LaunchConfig.FindBuiltIn(item.Name) is not null;
        var message = isOverride
            ? $"Remove the project override of '{item.Name}'? The built-in version will be used again."
            : $"Delete '{item.Name}' from Config/UETestTool.ini?";
        if (!_dialogs.Confirm("Delete configuration", message + "\n\nThis changes the project's shared config file.")) return;

        try
        {
            _configFile.Delete(item.Name);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't write {_configFile.Path}: {ex.Message}", isError: true);
            return;
        }
        ReloadConfigs(ReferenceEquals(item, SelectedConfig) ? (isOverride ? item.Name : null) : SelectedConfig?.Name,
            SelectedSession?.Name);
        SetStatus($"Deleted '{item.Name}'.");
    }

    [RelayCommand]
    private void OpenConfigFile()
    {
        if (SelectedProject is null) return;
        var path = ProjectFiles.SharedConfig(SelectedProject.Directory);
        if (File.Exists(path)) Launcher.OpenWithShell(path);
        else SetStatus("This project has no Config/UETestTool.ini yet. Add a configuration to create it.");
    }

    // ---- Sessions -----------------------------------------------------------------------------------

    [RelayCommand]
    private void NewSession() => EditSessionCore(null);

    [RelayCommand]
    private void EditSession(SessionItem? item)
    {
        if (item is not null) EditSessionCore(item);
    }

    private void EditSessionCore(SessionItem? item)
    {
        if (_configFile is null || SelectedProject is null)
        {
            SetStatus("Select a project first.", isError: true);
            return;
        }
        var edit = _dialogs.EditSession(item?.Session, Configs.Select(c => c.Config).ToList());
        if (edit is null) return;

        var existing = Sessions.FirstOrDefault(s => string.Equals(s.Name, edit.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !existing.IsProjectDefined)
        {
            if (!_dialogs.Confirm("Override built-in",
                    $"'{existing.Name}' is a built-in session.\n\nSaving will override it for everyone who uses this project's " +
                    "Config/UETestTool.ini. Deleting the override later restores the built-in.\n\nContinue?")) return;
        }
        else if (existing is not null && !ReferenceEquals(existing, item))
        {
            if (!_dialogs.Confirm("Replace session", $"Replace the existing session '{existing.Name}'?")) return;
        }

        var name = existing?.Name ?? edit.Name;
        try
        {
            _configFile.SaveSession(name, edit.Entries);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't write {_configFile.Path}: {ex.Message}", isError: true);
            return;
        }
        ReloadConfigs(SelectedConfig?.Name ?? _userState.LastConfig, name);
        RememberSelection();
        SaveUserStateSoon();
        SetStatus($"Saved session '{name}' to Config/UETestTool.ini.");
    }

    [RelayCommand]
    private void DeleteSession(SessionItem? item)
    {
        if (item is null || _configFile is null || !item.IsProjectDefined) return;
        var isOverride = LaunchSession.FindBuiltIn(item.Name) is not null;
        var message = isOverride
            ? $"Remove the project override of '{item.Name}'? The built-in version will be used again."
            : $"Delete the session '{item.Name}' from Config/UETestTool.ini?";
        if (!_dialogs.Confirm("Delete session", message + "\n\nThis changes the project's shared config file.")) return;

        try
        {
            _configFile.DeleteSession(item.Name);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't write {_configFile.Path}: {ex.Message}", isError: true);
            return;
        }
        var keep = ReferenceEquals(item, SelectedSession) ? (isOverride ? item.Name : null) : SelectedSession?.Name;
        ReloadConfigs(SelectedConfig?.Name ?? _userState.LastConfig, keep);
        RememberSelection();
        SaveUserStateSoon();
        SetStatus($"Deleted session '{item.Name}'.");
    }

    // ---- Launch -------------------------------------------------------------------------------------

    /// <summary>How long a session waits for its server to start listening before giving up on the clients.</summary>
    private static readonly TimeSpan ServerStartTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Gap between clients, so several editors don't all load at the same moment.</summary>
    private static readonly TimeSpan ClientStagger = TimeSpan.FromSeconds(1.5);

    private readonly LaunchedProcesses _processes = new();
    private CancellationTokenSource? _sessionCancel;

    /// <summary>A session's server has been started and the clients are waiting for it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isSessionStarting;

    private LaunchCommand? BuildCommand(LaunchConfig config, out string? error)
    {
        error = null;
        if (SelectedProject is not { IsAvailable: true } project) { error = "Select a project."; return null; }
        if (SelectedEngine is null) { error = "Select an engine."; return null; }
        var customArgs = Configs.FirstOrDefault(c => string.Equals(c.Name, config.Name, StringComparison.OrdinalIgnoreCase))?.CustomArgs;
        return LaunchCommandBuilder.Build(SelectedEngine.EditorExe, project.Path, config, SelectedMap?.PackagePath, customArgs, out error);
    }

    private void UpdateCommandPreview()
    {
        if (SelectedSession is { } session)
        {
            CommandPreview = SessionPreview(session);
            return;
        }
        if (SelectedConfig is null)
        {
            CommandPreview = SelectedProject is null ? "" : "Select a configuration.";
            return;
        }
        var command = BuildCommand(SelectedConfig.Config, out var error);
        CommandPreview = command?.DisplayText ?? error ?? "";
    }

    private string SessionPreview(SessionItem session)
    {
        var plan = session.Session.Resolve(Configs.Select(c => c.Config).ToList());
        if (plan.Error is not null) return $"Can't launch '{session.Name}': {plan.Error}";
        var server = BuildCommand(plan.Server!, out var error);
        if (server is null) return error ?? "";

        var lines = new List<string> { $"Server ({plan.Server!.Name}):", "  " + WithServerLog(server, ServerLogPathFor(server, prepare: false)).DisplayText };
        if (plan.Clients.Count > 0) lines.Add("Then, once it's listening, each client (the server's port is added to 127.0.0.1):");
        foreach (var client in plan.Clients)
            lines.Add($"  {BuildCommand(client, out var clientError)?.DisplayText ?? clientError}");
        return string.Join("\n", lines);
    }

    /// <summary>The commands a row launches, one per instance (a session's server first), or null with the reason.</summary>
    private IReadOnlyList<string>? CommandLines(ILaunchItem item, out string? error)
    {
        error = null;
        if (item is ConfigItem config)
            return BuildCommand(config.Config, out error) is { } command ? [command.DisplayText] : null;
        if (item is not SessionItem session) return null;

        var plan = session.Session.Resolve(Configs.Select(c => c.Config).ToList());
        if (plan.Error is not null)
        {
            error = $"Can't launch '{session.Name}': {plan.Error}";
            return null;
        }
        if (BuildCommand(plan.Server!, out error) is not { } server) return null;
        var lines = new List<string> { WithServerLog(server, ServerLogPathFor(server, prepare: false)).DisplayText };
        foreach (var client in plan.Clients)
        {
            if (BuildCommand(client, out error) is not { } command) return null;
            lines.Add(command.DisplayText);
        }
        return lines;
    }

    [RelayCommand]
    private void CopyCommandLine(ILaunchItem? item)
    {
        if (item is null) return;
        if (CommandLines(item, out var error) is not { } lines)
        {
            SetStatus(error ?? "Can't build the command line.", isError: true);
            return;
        }
        if (!_dialogs.CopyToClipboard(string.Join(Environment.NewLine, lines)))
        {
            SetStatus("Couldn't copy to the clipboard: another app is using it. Try again.", isError: true);
            return;
        }
        SetStatus(lines.Count == 1
            ? $"Copied the command line for '{item.Name}'."
            : $"Copied the {lines.Count} command lines for '{item.Name}', server first. Clients get the server's port added when launched.");
    }

    /// <summary>The server's log path: its own <c>-abslog=</c>, else one in the project's Saved/Logs that the tool adds.</summary>
    private string ServerLogPathFor(LaunchCommand server, bool prepare)
    {
        if (ServerLog.FindAbsLog(server.Arguments) is { } own)
            return Path.IsPathRooted(own) ? own : Path.GetFullPath(Path.Combine(server.WorkingDirectory, own));
        var logsDir = Path.Combine(SelectedProject!.Directory, "Saved", "Logs");
        var baseName = $"{SelectedProject.Name}-Server";
        return prepare ? ServerLog.PrepareLogPath(logsDir, baseName) : Path.Combine(logsDir, baseName + ".log");
    }

    private static LaunchCommand WithServerLog(LaunchCommand server, string logPath) =>
        ServerLog.FindAbsLog(server.Arguments) is not null
            ? server
            : server with { Arguments = $"{server.Arguments} {ServerLog.AbsLogArgument(logPath)}" };

    [RelayCommand]
    private async Task LaunchAsync()
    {
        FlushUserState();
        if (SelectedSession is { } session)
        {
            await LaunchSessionAsync(session);
            return;
        }
        if (SelectedConfig is not { } config)
        {
            SetStatus("Select a configuration.", isError: true);
            return;
        }
        var command = BuildCommand(config.Config, out var error);
        if (command is null)
        {
            SetStatus(error ?? "Can't launch.", isError: true);
            return;
        }
        try
        {
            _processes.Add(Launcher.Start(command), $"{SelectedProject!.Name}: {config.Name}");
            var where = config.Config.Rule.UsesMap ? SelectedMap?.Name ?? "default map" : config.Config.Rule.Connect;
            SetStatus($"Launched {SelectedProject.Name}: {config.Name} ({where}) at {DateTime.Now:HH:mm:ss}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Launch failed: {ex.Message}", isError: true);
        }
    }

    private async Task LaunchSessionAsync(SessionItem session)
    {
        var plan = session.Session.Resolve(Configs.Select(c => c.Config).ToList());
        if (plan.Error is not null)
        {
            SetStatus($"Can't launch '{session.Name}': {plan.Error}", isError: true);
            return;
        }
        var serverConfig = plan.Server!;
        var serverCommand = BuildCommand(serverConfig, out var error);
        foreach (var client in plan.Clients)
        {
            if (serverCommand is null) break;
            if (BuildCommand(client, out error) is null) serverCommand = null;
        }
        if (serverCommand is null)
        {
            SetStatus(error ?? "Can't launch.", isError: true);
            return;
        }

        var project = SelectedProject!;
        string logPath;
        long previousLogLength;
        Process server;
        try
        {
            // The tool's own log path is cleared first; a log the configuration chose itself may still hold the
            // previous run's "listening" line, so only what's written after its current end counts.
            logPath = ServerLogPathFor(serverCommand, prepare: true);
            previousLogLength = ServerLog.CurrentLength(logPath);
            server = Launcher.Start(WithServerLog(serverCommand, logPath));
        }
        catch (Exception ex)
        {
            SetStatus($"Launch failed: {ex.Message}", isError: true);
            return;
        }
        _processes.Add(server, $"{project.Name}: {serverConfig.Name}");

        using var cancel = new CancellationTokenSource();
        _sessionCancel = cancel;
        IsSessionStarting = true;
        var started = DateTime.Now;
        void ShowWaiting() =>
            SetStatus($"Waiting for {serverConfig.Name} to start listening… {(DateTime.Now - started).TotalSeconds:0} s. " +
                      $"{plan.Clients.Count} client(s) will follow.");
        ShowWaiting();
        var ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ticker.Tick += (_, _) => ShowWaiting();
        ticker.Start();
        try
        {
            var (result, port) = await ServerLog.WaitForListeningAsync(logPath, () => HasExited(server), ServerStartTimeout,
                cancel.Token, startPosition: previousLogLength);
            ticker.Stop();
            if (result == ServerWaitResult.Exited)
            {
                SetStatus($"{serverConfig.Name} closed before it started listening, so no clients were launched. Its log: {logPath}", isError: true);
                return;
            }
            if (result == ServerWaitResult.TimedOut)
            {
                SetStatus($"{serverConfig.Name} didn't start listening within {ServerStartTimeout.TotalMinutes:0} minutes, so no clients were " +
                          $"launched. It's still running; its log: {logPath}", isError: true);
                return;
            }

            for (var i = 0; i < plan.Clients.Count; i++)
            {
                if (i > 0) await Task.Delay(ClientStagger, cancel.Token);
                var client = plan.Clients[i];
                var command = BuildCommand(SessionPlan.ConnectingTo(client, port), out error)
                              ?? throw new InvalidOperationException(error);
                SetStatus($"{serverConfig.Name} is listening on port {port}. Launching client {i + 1} of {plan.Clients.Count}: {client.Name}…");
                _processes.Add(Launcher.Start(command), $"{project.Name}: {client.Name}");
            }
            SetStatus($"Launched {session.Name}: {serverConfig.Name} on port {port} and {plan.Clients.Count} client(s) at {DateTime.Now:HH:mm:ss}.");
        }
        catch (OperationCanceledException)
        {
            // Stop cancelled it, and reports what it stopped.
        }
        catch (Exception ex)
        {
            SetStatus($"Launching the clients failed: {ex.Message}", isError: true);
        }
        finally
        {
            ticker.Stop();
            _sessionCancel = null;
            IsSessionStarting = false;
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }   // disposed once it exited
    }

    // ---- Stop ---------------------------------------------------------------------------------------

    public string StopText => _processes.Count > 0 ? $"Stop ({_processes.Count})" : "Stop";

    public string StopToolTip => _processes.Count == 0
        ? "Close everything launched from here. Nothing is running."
        : "Close everything launched from here:\n" + string.Join("\n", _processes.Labels);

    private bool CanStop() => _processes.Count > 0 || IsSessionStarting;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        var wasStarting = IsSessionStarting;
        _sessionCancel?.Cancel();
        SetStatus("Stopping…");
        var stopped = await _processes.StopAllAsync(TimeSpan.FromSeconds(5));
        SetStatus(stopped == 0 && wasStarting
            ? "Cancelled the session."
            : $"Stopped {stopped} instance(s){(wasStarting ? " and cancelled the session's remaining clients" : "")}.");
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    private void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        StatusIsError = isError;
    }

    private void SaveUserStateSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending per-project state now (called on a timer, project switch, launch and exit).</summary>
    public void FlushUserState()
    {
        if (!_saveTimer.IsEnabled) return;
        _saveTimer.Stop();
        if (_userStatePath is null) return;
        try
        {
            _userState.Save(_userStatePath);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't save {_userStatePath}: {ex.Message}", isError: true);
        }
    }
}
