namespace Drs.Unreal.Common.Projects;

public enum ProjectAvailability
{
    /// <summary>The .uproject exists.</summary>
    Available,

    /// <summary>The drive/share is reachable but the .uproject is gone: the entry should be dropped.</summary>
    Missing,

    /// <summary>The drive or network share itself can't be reached (e.g. unplugged USB drive): keep, show greyed out.</summary>
    Unavailable,
}

/// <summary>One remembered project. Plain settable properties so it serialises with System.Text.Json.</summary>
public sealed class ProjectCacheEntry
{
    public string Path { get; set; } = "";
    public ProjectSource Source { get; set; }
}

/// <summary>
/// The app's memory of the projects in its list: those found in the user's search folders, plus those added
/// with Browse…. It keeps projects on drives that are currently unreachable (shown greyed out). An entry is
/// removed when its .uproject is confirmed gone, when it is no longer inside any search folder (see
/// <see cref="Prune"/>), or when the user removes it (which also ignores it so discovery doesn't re-add it).
/// </summary>
public sealed class ProjectCache
{
    public List<ProjectCacheEntry> Projects { get; set; } = [];

    /// <summary>Projects the user removed from the list; discovery won't re-add these, Browse will.</summary>
    public List<string> Ignored { get; set; } = [];

    /// <summary>Adds newly discovered projects. Never removes anything.</summary>
    /// <returns>Number of projects added.</returns>
    public int Merge(IEnumerable<DiscoveredProject> discovered)
    {
        var known = Projects.Select(p => p.Path).ToHashSet(PathUtil.Comparer);
        var ignored = Ignored.ToHashSet(PathUtil.Comparer);
        var added = 0;
        foreach (var d in discovered)
        {
            if (ignored.Contains(d.UProjectPath) || !known.Add(d.UProjectPath)) continue;
            Projects.Add(new ProjectCacheEntry { Path = d.UProjectPath, Source = d.Source });
            added++;
        }
        return added;
    }

    /// <summary>
    /// Drops discovered entries that aren't inside any of <paramref name="searchFolders"/> (e.g. a folder was
    /// removed from the list). Projects added with Browse… are kept.
    /// </summary>
    /// <returns>Number of projects dropped.</returns>
    public int Prune(IEnumerable<string> searchFolders)
    {
        var folders = searchFolders.Select(f => { try { return PathUtil.Normalize(f); } catch (Exception) { return null; } })
            .OfType<string>().ToList();
        return Projects.RemoveAll(p => p.Source != ProjectSource.Browsed && !folders.Any(f => PathUtil.IsUnder(p.Path, f)));
    }

    /// <summary>Adds (or un-ignores) a project the user picked with Browse….</summary>
    public ProjectCacheEntry AddBrowsed(string uprojectPath)
    {
        var path = PathUtil.Normalize(uprojectPath);
        Ignored.RemoveAll(i => PathUtil.Comparer.Equals(i, path));
        var existing = Find(path);
        if (existing is not null)
        {
            existing.Source = ProjectSource.Browsed;
            return existing;
        }
        var entry = new ProjectCacheEntry { Path = path, Source = ProjectSource.Browsed };
        Projects.Add(entry);
        return entry;
    }

    /// <summary>"Remove from list": drops the entry and ignores it for future discovery.</summary>
    public void Remove(string uprojectPath)
    {
        var path = PathUtil.Normalize(uprojectPath);
        Projects.RemoveAll(p => PathUtil.Comparer.Equals(p.Path, path));
        if (!Ignored.Contains(path, PathUtil.Comparer)) Ignored.Add(path);
    }

    public ProjectCacheEntry? Find(string uprojectPath) =>
        Projects.FirstOrDefault(p => PathUtil.Comparer.Equals(p.Path, uprojectPath));

    /// <summary>
    /// Drops entries whose .uproject is gone and returns the availability of the rest.
    /// Entries on unreachable drives/shares are kept.
    /// </summary>
    public IReadOnlyDictionary<string, ProjectAvailability> Validate(Func<string, ProjectAvailability>? check = null)
    {
        check ??= CheckAvailability;
        var result = new Dictionary<string, ProjectAvailability>(PathUtil.Comparer);
        foreach (var entry in Projects.ToList())
        {
            var availability = check(entry.Path);
            if (availability == ProjectAvailability.Missing) Projects.Remove(entry);
            else result[entry.Path] = availability;
        }
        return result;
    }

    public static ProjectAvailability CheckAvailability(string uprojectPath)
    {
        try
        {
            if (File.Exists(uprojectPath)) return ProjectAvailability.Available;
            var root = Path.GetPathRoot(uprojectPath);
            return !string.IsNullOrEmpty(root) && Directory.Exists(root)
                ? ProjectAvailability.Missing
                : ProjectAvailability.Unavailable;
        }
        catch (Exception)
        {
            return ProjectAvailability.Unavailable;
        }
    }
}
