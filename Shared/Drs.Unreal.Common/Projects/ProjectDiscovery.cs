using System.Text.RegularExpressions;

namespace Drs.Unreal.Common.Projects;

/// <summary>How a project came to be known. Ordered by "strength": a browsed project stays Browsed.</summary>
public enum ProjectSource
{
    Recent,
    SearchFolder,
    Browsed,
}

public sealed record DiscoveredProject(string UProjectPath, ProjectSource Source);

/// <summary>
/// Finds .uproject files from the places UE itself records them, plus user-chosen search folders.
/// There is no global UE project registry, so this deliberately avoids whole-drive scans:
/// <list type="number">
/// <item><c>RecentlyOpenedProjectFiles</c> in every <c>%LOCALAPPDATA%\UnrealEngine\*\Saved\Config\*Editor\EditorSettings.ini</c>.</item>
/// <item>A shallow scan of each <c>CreatedProjectPaths</c> entry in those same files.</item>
/// <item>A shallow scan of user-added search folders.</item>
/// </list>
/// Results are not durable on their own (UE's lists are per engine version and get lost); the
/// caller merges them into a <see cref="ProjectCache"/>.
/// </summary>
public static partial class ProjectDiscovery
{
    /// <summary>Default depth for folder scans: root, root\X, root\X\Y.</summary>
    public const int DefaultScanDepth = 2;

    /// <summary>
    /// Build-output and system folders that never contain projects worth finding. Names a user might give a
    /// projects folder (e.g. "Source", "Content") are deliberately absent: folders inside a project are never
    /// entered anyway, because the scan stops at any folder that contains a .uproject.
    /// </summary>
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Binaries", "Intermediate", "Saved", "DerivedDataCache",
        "node_modules", "Windows", "Program Files", "Program Files (x86)", "ProgramData",
    };

    public static string UnrealLocalAppData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnrealEngine");

    public sealed record EditorSettingsInfo(IReadOnlyList<string> RecentProjects, IReadOnlyList<string> CreatedProjectPaths);

    /// <param name="engineRoots">Projects inside these folders (engine templates/samples) are excluded.</param>
    public static IReadOnlyList<DiscoveredProject> Discover(
        IEnumerable<string> searchFolders,
        IEnumerable<string> engineRoots,
        string? unrealLocalAppData = null)
    {
        var roots = engineRoots.ToList();
        var found = new Dictionary<string, DiscoveredProject>(PathUtil.Comparer);

        void Add(string path, ProjectSource source)
        {
            string full;
            try { full = PathUtil.Normalize(path); } catch (Exception) { return; }
            if (!full.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase)) return;
            if (roots.Any(r => PathUtil.IsUnder(full, r))) return;
            if (!File.Exists(full)) return;
            found.TryAdd(full, new DiscoveredProject(full, source));
        }

        var settings = ReadAllEditorSettings(unrealLocalAppData ?? UnrealLocalAppData);
        foreach (var p in settings.RecentProjects) Add(p, ProjectSource.Recent);
        foreach (var dir in settings.CreatedProjectPaths)
            foreach (var p in ScanFolder(dir)) Add(p, ProjectSource.Recent);
        foreach (var dir in searchFolders)
            foreach (var p in ScanFolder(dir)) Add(p, ProjectSource.SearchFolder);

        return found.Values.ToList();
    }

    /// <summary>Reads and merges EditorSettings.ini from every engine version's user folder.</summary>
    public static EditorSettingsInfo ReadAllEditorSettings(string unrealLocalAppData)
    {
        var recent = new List<string>();
        var created = new List<string>();
        foreach (var versionDir in PathUtil.SafeDirectories(unrealLocalAppData))
        {
            var configDir = Path.Combine(versionDir, "Saved", "Config");
            foreach (var platformDir in PathUtil.SafeDirectories(configDir))
            {
                // WindowsEditor (UE5), Windows (some UE4 versions)
                var ini = Path.Combine(platformDir, "EditorSettings.ini");
                if (!File.Exists(ini)) continue;
                try
                {
                    var info = ParseEditorSettings(File.ReadAllText(ini));
                    recent.AddRange(info.RecentProjects);
                    created.AddRange(info.CreatedProjectPaths);
                }
                catch (Exception) { }
            }
        }
        return new EditorSettingsInfo(
            recent.Distinct(PathUtil.Comparer).ToList(),
            created.Distinct(PathUtil.Comparer).ToList());
    }

    /// <summary>
    /// Handles both the UE5 struct form <c>RecentlyOpenedProjectFiles=(ProjectName="D:/X/X.uproject",LastOpenTime=...)</c>
    /// and the older plain form <c>RecentlyOpenedProjectFiles=D:/X/X.uproject</c>.
    /// </summary>
    internal static EditorSettingsInfo ParseEditorSettings(string iniText)
    {
        var recent = new List<string>();
        var created = new List<string>();
        foreach (var rawLine in iniText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (TryValue(line, "RecentlyOpenedProjectFiles", out var value))
            {
                var m = ProjectNameRegex().Match(value);
                var path = m.Success ? m.Groups[1].Value : value.Trim('"');
                if (path.Length > 0 && !path.StartsWith('(')) recent.Add(path);
            }
            else if (TryValue(line, "CreatedProjectPaths", out value))
            {
                value = value.Trim('"');
                if (value.Length > 0) created.Add(value);
            }
        }
        return new EditorSettingsInfo(recent, created);
    }

    private static bool TryValue(string line, string key, out string value)
    {
        value = "";
        // Accept "Key=", "+Key=" (array add) forms.
        var start = line.StartsWith('+') ? 1 : 0;
        if (line.Length <= start + key.Length || !line.AsSpan(start).StartsWith(key, StringComparison.OrdinalIgnoreCase)) return false;
        if (line[start + key.Length] != '=') return false;
        value = line[(start + key.Length + 1)..].Trim();
        return true;
    }

    [GeneratedRegex("ProjectName=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectNameRegex();

    /// <summary>
    /// Finds .uproject files in <paramref name="root"/> and its subfolders down to <paramref name="depth"/> levels.
    /// Does not look inside a folder that is itself a project.
    /// </summary>
    public static IEnumerable<string> ScanFolder(string root, int depth = DefaultScanDepth)
    {
        if (string.IsNullOrWhiteSpace(root)) yield break;
        string start;
        try { start = PathUtil.Normalize(root); } catch (Exception) { yield break; }
        if (!Directory.Exists(start)) yield break;

        var walk = DirectoryWalk.Walk(start, depth,
            // Don't look inside a project (its subfolders hold no other projects) or an engine tree (templates/samples).
            descendInto: dir => PathUtil.SafeFiles(dir, "*.uproject").Count() == 0 && !IsEngineRoot(dir),
            includeChild: sub =>
            {
                var name = Path.GetFileName(sub);
                return !name.StartsWith('.') && !name.StartsWith('$') && !SkipDirs.Contains(name);
            });

        foreach (var dir in walk)
            foreach (var p in PathUtil.SafeFiles(dir, "*.uproject"))
                yield return p;
    }

    private static bool IsEngineRoot(string dir) => File.Exists(Path.Combine(dir, "Engine", "Build", "Build.version"));
}
