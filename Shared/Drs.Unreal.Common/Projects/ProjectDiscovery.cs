namespace Drs.Unreal.Common.Projects;

/// <summary>How a project came to be known. Ordered by "strength": a browsed project stays Browsed.</summary>
public enum ProjectSource
{
    /// <summary>Found by earlier versions in Unreal's recent-project lists. Treated like <see cref="SearchFolder"/>.</summary>
    Recent,

    /// <summary>Found in one of the user's search folders.</summary>
    SearchFolder,

    /// <summary>Added by the user with Browse….</summary>
    Browsed,
}

public sealed record DiscoveredProject(string UProjectPath, ProjectSource Source);

/// <summary>
/// Finds .uproject files in the folders the user chose to search, and nowhere else: a shallow scan of each
/// folder (see <see cref="DefaultScanDepth"/>). The caller merges the results into a <see cref="ProjectCache"/>.
/// </summary>
public static class ProjectDiscovery
{
    /// <summary>
    /// Default depth for folder scans: projects in root\X, root\X\Y or root\X\Y\Z (e.g. Repos\Org\Repo\Game).
    /// Cheap, because the scan never enters a project folder, an engine tree or build output.
    /// </summary>
    public const int DefaultScanDepth = 3;

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

    /// <param name="engineRoots">Projects inside these folders (engine templates/samples) are excluded.</param>
    public static IReadOnlyList<DiscoveredProject> Discover(IEnumerable<string> searchFolders, IEnumerable<string> engineRoots)
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

        foreach (var dir in searchFolders)
            foreach (var p in ScanFolder(dir)) Add(p, ProjectSource.SearchFolder);

        return found.Values.ToList();
    }

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
