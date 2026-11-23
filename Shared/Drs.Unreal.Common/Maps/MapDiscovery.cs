using System.Text.Json;

namespace Drs.Unreal.Common.Maps;

/// <param name="PackagePath">What UE expects on the command line, e.g. <c>/Game/Maps/MyMap</c> or <c>/MyPlugin/Maps/Test</c>.</param>
/// <param name="FilePath">The .umap file on disk.</param>
/// <param name="PluginName">The content plugin it belongs to, or null for the project's own Content folder.</param>
public sealed record MapInfo(string PackagePath, string FilePath, string? PluginName)
{
    public string Name => Path.GetFileNameWithoutExtension(FilePath);
    public override string ToString() => PackagePath;
}

/// <summary>
/// Lists the maps in a project: <c>&lt;Project&gt;\Content\**\*.umap</c> mounted at <c>/Game</c>, plus the
/// Content folder of every project plugin with <c>"CanContainContent": true</c>, mounted at <c>/&lt;PluginName&gt;</c>.
/// </summary>
public static class MapDiscovery
{
    /// <summary>
    /// One-file-per-actor data folders hold thousands of small files and never contain .umap files;
    /// skipping them keeps scans of World Partition projects fast.
    /// </summary>
    private static readonly HashSet<string> SkipContentDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "__ExternalActors__", "__ExternalObjects__", "Collections",
    };

    private static readonly HashSet<string> SkipPluginSearchDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Binaries", "Intermediate", "Saved", "Source", "Content", "Resources", "Config", "Shaders", ".git",
    };

    public static IReadOnlyList<MapInfo> Discover(string projectDir)
    {
        var maps = new List<MapInfo>();
        maps.AddRange(ScanContent(Path.Combine(projectDir, "Content"), "/Game", null)
            .OrderBy(m => m.PackagePath, StringComparer.OrdinalIgnoreCase));

        foreach (var (name, pluginDir) in FindContentPlugins(Path.Combine(projectDir, "Plugins"))
                     .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            maps.AddRange(ScanContent(Path.Combine(pluginDir, "Content"), "/" + name, name)
                .OrderBy(m => m.PackagePath, StringComparer.OrdinalIgnoreCase));
        }
        return maps;
    }

    /// <summary>Finds .uplugin files (plugins may be nested in category folders) whose descriptor allows content.</summary>
    internal static IEnumerable<(string Name, string Dir)> FindContentPlugins(string pluginsDir)
    {
        if (!Directory.Exists(pluginsDir)) yield break;
        var walk = DirectoryWalk.Walk(pluginsDir, MaxDepth,
            // A folder with a .uplugin is a plugin root; plugins don't nest inside each other.
            descendInto: dir => !PathUtil.SafeFiles(dir, "*.uplugin").Any(),
            includeChild: sub => !SkipPluginSearchDirs.Contains(Path.GetFileName(sub)));
        foreach (var dir in walk)
            foreach (var d in PathUtil.SafeFiles(dir, "*.uplugin"))
                if (CanContainContent(d)) yield return (Path.GetFileNameWithoutExtension(d), dir);
    }

    /// <summary>Safety bound for recursive scans (real content trees are far shallower).</summary>
    private const int MaxDepth = 64;

    internal static bool CanContainContent(string upluginPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(upluginPath), Json.LenientOptions);
            return doc.RootElement.TryGetProperty("CanContainContent", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch (Exception) { return false; }
    }

    internal static IEnumerable<MapInfo> ScanContent(string contentDir, string mountPoint, string? pluginName)
    {
        if (!Directory.Exists(contentDir)) yield break;
        var walk = DirectoryWalk.Walk(contentDir, MaxDepth,
            includeChild: sub => !SkipContentDirs.Contains(Path.GetFileName(sub)));
        foreach (var dir in walk)
            foreach (var file in PathUtil.SafeFiles(dir, "*.umap"))
                yield return new MapInfo(ToPackagePath(contentDir, file, mountPoint), file, pluginName);
    }

    /// <summary><c>…\Content\Maps\Test.umap</c> with mount <c>/Game</c> → <c>/Game/Maps/Test</c>.</summary>
    internal static string ToPackagePath(string contentDir, string umapPath, string mountPoint)
    {
        var relative = Path.GetRelativePath(contentDir, umapPath);
        var withoutExt = Path.ChangeExtension(relative, null)!;
        return mountPoint.TrimEnd('/') + "/" + withoutExt.Replace('\\', '/');
    }
}
