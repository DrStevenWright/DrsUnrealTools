using System.Text.Json;

namespace Drs.Unreal.Common;

internal static class Json
{
    /// <summary>UE's JSON files (.uproject, .uplugin) are hand-edited and may contain comments or trailing commas.</summary>
    public static readonly JsonDocumentOptions LenientOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

public static class PathUtil
{
    /// <summary>
    /// Normalises a path for comparison and storage: full path, backslashes, no trailing separator
    /// (except for a drive root such as <c>D:\</c>).
    /// </summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        var root = Path.GetPathRoot(full) ?? "";
        return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
    }

    public static bool IsUnder(string path, string root)
    {
        static string WithSlash(string s) => s.EndsWith('\\') ? s : s + "\\";
        return WithSlash(Normalize(path)).StartsWith(WithSlash(Normalize(root)), StringComparison.OrdinalIgnoreCase);
    }

    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    // Reparse points are deliberately NOT skipped: OneDrive "Files On-Demand" files and folders (e.g. a
    // redirected Documents\Unreal Projects) are reparse points, as are symlinked Content folders.
    // Loops through junctions/symlinks are prevented by DirectoryWalk's visited set instead.
    private static readonly EnumerationOptions DirOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.System,   // $RECYCLE.BIN, System Volume Information
    };

    private static readonly EnumerationOptions FileOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
    };

    internal static IEnumerable<string> SafeDirectories(string dir)
    {
        try { return Directory.EnumerateDirectories(dir, "*", DirOptions).ToList(); }
        catch (Exception) { return []; }
    }

    internal static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern, FileOptions).ToList(); }
        catch (Exception) { return []; }
    }

    /// <summary>Identity of a directory for loop detection: the final target if it is a link, else its own path.</summary>
    internal static string DirectoryKey(string dir)
    {
        try
        {
            var target = new DirectoryInfo(dir).ResolveLinkTarget(returnFinalTarget: true);
            return Normalize(target?.FullName ?? dir);
        }
        catch (Exception) { return Normalize(dir); }
    }
}

/// <summary>Breadth-first directory walk that tolerates links (no infinite loops) and inaccessible folders.</summary>
internal static class DirectoryWalk
{
    /// <param name="descendInto">Whether to look inside a visited directory at all.</param>
    /// <param name="includeChild">Filter for subdirectories (e.g. by name).</param>
    public static IEnumerable<string> Walk(
        string root, int maxDepth, Func<string, bool>? descendInto = null, Func<string, bool>? includeChild = null)
    {
        var visited = new HashSet<string>(PathUtil.Comparer);
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (!visited.Add(PathUtil.DirectoryKey(dir))) continue;
            yield return dir;
            if (depth >= maxDepth || (descendInto is not null && !descendInto(dir))) continue;
            foreach (var sub in PathUtil.SafeDirectories(dir))
                if (includeChild is null || includeChild(sub)) queue.Enqueue((sub, depth + 1));
        }
    }
}
