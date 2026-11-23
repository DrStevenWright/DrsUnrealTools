using System.Collections.ObjectModel;
using System.IO;
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
    }

    public ObservableCollection<ProjectItem> Projects { get; } = [];
    public ObservableCollection<EngineInstall> Engines { get; } = [];
    public ObservableCollection<MapInfo> Maps { get; } = [];
    public ObservableCollection<ConfigItem> Configs { get; } = [];

    [ObservableProperty] private ProjectItem? _selectedProject;
    [ObservableProperty] private EngineInstall? _selectedEngine;
    [ObservableProperty] private MapInfo? _selectedMap;
    [ObservableProperty] private ConfigItem? _selectedConfig;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusIsError;

    /// <summary>Shown when the selected engine isn't the one the project is associated with.</summary>
    [ObservableProperty] private string? _engineWarning;
    [ObservableProperty] private EngineInstall? _suggestedEngine;

    /// <summary>Problems in the project's UETestTool.ini (unknown templates etc.).</summary>
    [ObservableProperty] private string? _configWarnings;

    [ObservableProperty] private bool _showCommand;
    [ObservableProperty] private string _commandPreview = "";

    public bool IsMapEnabled => SelectedConfig?.Config.Rule.UsesMap ?? true;

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

    private async Task RefreshAsync(string? selectPath)
    {
        if (IsBusy) return;
        IsBusy = true;
        SetStatus("Searching for engines and projects…");
        FlushUserState();

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
        var availabilityOfKept = cache.Validate(p => result.availability.GetValueOrDefault(p, ProjectAvailability.Unavailable));
        var removed = countBefore - cache.Projects.Count;

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
        SetStatus(Engines.Count == 0 ? "No Unreal Engine installations found." : summary + ".", isError: Engines.Count == 0);
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
            ReloadConfigs(_userState.LastConfig);

            // Engine: the one last used with this project, else the project's own association, else the last one used anywhere.
            var associated = EngineDiscovery.ResolveAssociation(project.File.EngineAssociation, project.Path, Engines);
            SelectedEngine = Engines.FirstOrDefault(e => e.Identifier == _userState.LastEngine)
                             ?? associated
                             ?? Engines.FirstOrDefault(e => e.Identifier == _settings.LastEngine)
                             ?? Engines.FirstOrDefault();
            UpdateEngineWarning();
        }
        finally
        {
            _loading = false;
            OnPropertyChanged(nameof(SharedConfigPath));
            OnPropertyChanged(nameof(SaveConfigButtonText));
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

    private void ReloadConfigs(string? select)
    {
        if (_configFile is null) return;
        var wasLoading = _loading;
        _loading = true;
        // Clearing the collection makes the bound list and template dropdown push null selections back;
        // remember the template by name and suppress its args prefill while rebuilding.
        var templateName = NewConfigTemplate?.Name;
        _suppressTemplateArgs = true;
        try
        {
            Configs.Clear();
            foreach (var config in _configFile.GetConfigs())
                Configs.Add(new ConfigItem(config, _userState.GetCustomArgs(config.Name), _configFile.Defines(config.Name), OnCustomArgsChanged));
            ConfigWarnings = _configFile.Warnings.Count == 0 ? null : string.Join("\n", _configFile.Warnings);
            SelectedConfig = Configs.FirstOrDefault(c => string.Equals(c.Name, select, StringComparison.OrdinalIgnoreCase))
                             ?? Configs.FirstOrDefault();
            NewConfigTemplate = Configs.FirstOrDefault(c => c.Name == templateName) ?? Configs.FirstOrDefault();
        }
        finally
        {
            _suppressTemplateArgs = false;
            _loading = wasLoading;
            OnPropertyChanged(nameof(SaveConfigButtonText));
        }
        // First load (or after Add cleared the row): prefill from the template.
        if (string.IsNullOrWhiteSpace(NewConfigArgs) && NewConfigTemplate is not null) NewConfigArgs = NewConfigTemplate.Args;
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

    partial void OnSelectedConfigChanged(ConfigItem? value)
    {
        OnPropertyChanged(nameof(IsMapEnabled));
        OnPropertyChanged(nameof(MapHint));
        UpdateCommandPreview();
        if (_loading || value is null) return;
        _userState.LastConfig = value.Name;
        SaveUserStateSoon();
    }

    private void OnCustomArgsChanged(ConfigItem item)
    {
        if (ReferenceEquals(item, SelectedConfig)) UpdateCommandPreview();
        if (_loading) return;
        _userState.CustomArgs[item.Name] = item.CustomArgs;
        SaveUserStateSoon();
    }

    private void UpdateEngineWarning()
    {
        SuggestedEngine = null;
        EngineWarning = null;
        if (SelectedProject is not { IsAvailable: true } project) return;

        var association = project.File.EngineAssociation;
        var expected = EngineDiscovery.ResolveAssociation(association, project.Path, Engines);
        if (expected is not null)
        {
            if (!ReferenceEquals(expected, SelectedEngine))
            {
                EngineWarning = $"{project.Name} is associated with {expected.DisplayName}.";
                SuggestedEngine = expected;
            }
        }
        else if (!string.IsNullOrEmpty(association))
        {
            EngineWarning = association.StartsWith('{')
                ? $"{project.Name} is associated with a source build ({association}) that isn't registered on this machine."
                : $"{project.Name} is associated with UE {association}, which isn't installed.";
        }
    }

    [RelayCommand]
    private void SwitchEngine()
    {
        if (SuggestedEngine is not null) SelectedEngine = SuggestedEngine;
    }

    // ---- New / edit / delete configurations ----------------------------------------------------------

    [ObservableProperty] private string _newConfigName = "";
    [ObservableProperty] private ConfigItem? _newConfigTemplate;
    [ObservableProperty] private string _newConfigArgs = "";
    [ObservableProperty] private string? _newConfigError;

    public string SaveConfigButtonText =>
        Configs.Any(c => string.Equals(c.Name, NewConfigName.Trim(), StringComparison.OrdinalIgnoreCase)) ? "Save" : "Add";

    partial void OnNewConfigNameChanged(string value)
    {
        NewConfigError = null;
        OnPropertyChanged(nameof(SaveConfigButtonText));
    }

    partial void OnNewConfigTemplateChanged(ConfigItem? value)
    {
        if (value is not null && !_suppressTemplateArgs) NewConfigArgs = value.Args;
    }

    private bool _suppressTemplateArgs;

    [RelayCommand]
    private void EditConfig(ConfigItem? item)
    {
        if (item is null) return;
        // Using the config itself as the template keeps its rule; saving under the same name updates it
        // (or, for a built-in, creates a project override).
        _suppressTemplateArgs = true;
        NewConfigTemplate = item;
        _suppressTemplateArgs = false;
        NewConfigName = item.Name;
        NewConfigArgs = item.Args;
        NewConfigError = null;
    }

    [RelayCommand]
    private void SaveConfig()
    {
        if (_configFile is null || SelectedProject is null)
        {
            NewConfigError = "Select a project first.";
            return;
        }
        var name = NewConfigName.Trim();
        if (LaunchConfig.ValidateName(name) is { } error)
        {
            NewConfigError = error;
            return;
        }
        if (NewConfigTemplate is null)
        {
            NewConfigError = "Choose a template.";
            return;
        }

        var existing = Configs.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !existing.IsProjectDefined)
        {
            if (!_dialogs.Confirm("Override built-in",
                    $"'{existing.Name}' is a built-in configuration.\n\nSaving will override it for everyone who uses this project's " +
                    $"Config/UETestTool.ini. Deleting the override later restores the built-in.\n\nContinue?")) return;
        }
        else if (existing is not null && !ReferenceEquals(existing, NewConfigTemplate))
        {
            if (!_dialogs.Confirm("Replace configuration", $"Replace the existing configuration '{existing.Name}'?")) return;
        }

        try
        {
            _configFile.Save(existing?.Name ?? name, NewConfigTemplate.Config, NewConfigArgs);
        }
        catch (Exception ex)
        {
            NewConfigError = $"Couldn't write {_configFile.Path}: {ex.Message}";
            return;
        }

        ReloadConfigs(existing?.Name ?? name);
        if (!_loading)
        {
            _userState.LastConfig = SelectedConfig?.Name;
            SaveUserStateSoon();
        }
        NewConfigName = "";
        NewConfigError = null;
        SetStatus($"Saved '{existing?.Name ?? name}' to Config/UETestTool.ini.");
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
        ReloadConfigs(ReferenceEquals(item, SelectedConfig) ? (isOverride ? item.Name : null) : SelectedConfig?.Name);
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

    // ---- Launch -------------------------------------------------------------------------------------

    private LaunchCommand? BuildCommand(out string? error)
    {
        error = null;
        if (SelectedProject is not { IsAvailable: true } project) { error = "Select a project."; return null; }
        if (SelectedEngine is null) { error = "Select an engine."; return null; }
        if (SelectedConfig is null) { error = "Select a configuration."; return null; }
        return LaunchCommandBuilder.Build(SelectedEngine.EditorExe, project.Path, SelectedConfig.Config,
            SelectedMap?.PackagePath, SelectedConfig.CustomArgs, out error);
    }

    private void UpdateCommandPreview()
    {
        var command = BuildCommand(out var error);
        CommandPreview = command?.DisplayText ?? error ?? "";
    }

    [RelayCommand]
    private void Launch()
    {
        FlushUserState();
        var command = BuildCommand(out var error);
        if (command is null)
        {
            SetStatus(error ?? "Can't launch.", isError: true);
            return;
        }
        try
        {
            Launcher.Start(command);
            var where = SelectedConfig!.Config.Rule.UsesMap ? SelectedMap?.Name ?? "default map" : SelectedConfig.Config.Rule.Connect;
            SetStatus($"Launched {SelectedProject!.Name}: {SelectedConfig.Name} ({where}) at {DateTime.Now:HH:mm:ss}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Launch failed: {ex.Message}", isError: true);
        }
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
