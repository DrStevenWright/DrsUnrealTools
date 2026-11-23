using System.Text.RegularExpressions;

namespace Drs.Unreal.Common.Engines;

/// <summary>Where a project's engine version was worked out from.</summary>
public enum ProjectVersionSource
{
    /// <summary>Not known: no association and not inside an engine tree, or an association that isn't a version or GUID.</summary>
    Unknown,

    /// <summary>A source-build GUID that isn't registered on this machine, and no project log to go by.</summary>
    UnregisteredBuild,

    /// <summary>The association is a version number such as <c>5.8</c>.</summary>
    Association,

    /// <summary>The association is the GUID of a source build registered on this machine.</summary>
    RegisteredBuild,

    /// <summary>No association: the project lives inside an engine tree.</summary>
    ContainingEngine,

    /// <summary>Unregistered source-build GUID: the version is taken from the project's most recent log.</summary>
    ProjectLog,
}

/// <param name="Version">The engine version the project expects, if known.</param>
/// <param name="Associated">The engine the association names exactly (as UE resolves it), if installed.</param>
public sealed record ProjectEngineInfo(EngineVersion? Version, ProjectVersionSource Source, EngineInstall? Associated);

/// <param name="Warning">Shown under the engine selector, or null when the selected engine is fine.</param>
/// <param name="Suggested">An installed engine of the version the project expects, offered by a Switch button.</param>
public sealed record EngineCheck(string? Warning, EngineInstall? Suggested)
{
    public static readonly EngineCheck Ok = new(null, null);
}

/// <summary>
/// Works out which engine version a project expects and whether the selected engine suits it. Engines are compared by
/// release (major.minor) only: a project made with a 5.8 source build runs on the 5.8 launcher install and vice versa,
/// and patch releases are compatible.
/// </summary>
public static partial class ProjectEngine
{
    /// <summary>
    /// The project's engine version, from the first of these that gives one:
    /// <list type="number">
    /// <item>The .uproject's <c>EngineAssociation</c>, if it is a version such as <c>5.8</c>.</item>
    /// <item>The source build the association's GUID names, if it is registered on this machine.</item>
    /// <item>The project's most recent log (<c>Saved\Logs</c>), which records the engine version that wrote it.</item>
    /// </list>
    /// Otherwise the version is unknown. (A project with no association uses the engine whose tree contains it.)
    /// </summary>
    public static ProjectEngineInfo Identify(
        string? association, string uprojectPath, IReadOnlyCollection<EngineInstall> engines,
        Func<string, EngineVersion?>? readLogVersion = null)
    {
        var associated = EngineDiscovery.ResolveAssociation(association, uprojectPath, engines);
        if (string.IsNullOrWhiteSpace(association))
            return new(associated?.Version, associated?.Version is null ? ProjectVersionSource.Unknown : ProjectVersionSource.ContainingEngine, associated);

        if (ParseAssociationVersion(association) is { } version)
            return new(version, ProjectVersionSource.Association, associated);

        if (associated?.Version is { } built)
            return new(built, ProjectVersionSource.RegisteredBuild, associated);

        if (associated is null && association.TrimStart().StartsWith('{'))
        {
            var projectDir = Path.GetDirectoryName(uprojectPath) ?? "";
            return (readLogVersion ?? ReadVersionFromLog)(projectDir) is { } logged
                ? new(logged, ProjectVersionSource.ProjectLog, associated)
                : new(null, ProjectVersionSource.UnregisteredBuild, associated);
        }

        return new(null, ProjectVersionSource.Unknown, associated);
    }

    /// <summary>The engine to pick for the project: the exact association, else the best installed engine of the same release.</summary>
    public static EngineInstall? Preferred(ProjectEngineInfo info, IEnumerable<EngineInstall> engines) =>
        info.Associated ?? (info.Version is { } v ? engines.FirstOrDefault(e => e.Version?.SameRelease(v) == true) : null);

    public static EngineCheck Check(string projectName, ProjectEngineInfo info, EngineInstall? selected, IEnumerable<EngineInstall> engines)
    {
        if (selected is null || ReferenceEquals(selected, info.Associated)) return EngineCheck.Ok;

        if (info.Version is not { } expected)
        {
            return info.Source == ProjectVersionSource.UnregisteredBuild
                ? new($"{projectName} is associated with a source build that isn't registered on this machine, and has no log " +
                      "showing which engine version it uses. Its engine version is unknown.", null)
                : EngineCheck.Ok;
        }

        if (selected.Version is not { } actual || actual.SameRelease(expected)) return EngineCheck.Ok;

        var expectedText = $"UE {expected.Release}";
        var from = info.Source == ProjectVersionSource.ProjectLog ? " (going by its most recent log)" : "";
        var suggested = Preferred(info, engines);
        var tail = suggested is null ? $" {expectedText} isn't installed." : "";
        return new($"{projectName} uses {expectedText}{from}, but the selected engine is UE {actual.Release}.{tail}", suggested);
    }

    /// <summary><c>5.8</c> or <c>4.27</c> → that version; anything else (a GUID, garbage) → null.</summary>
    internal static EngineVersion? ParseAssociationVersion(string association)
    {
        var m = AssociationRegex().Match(association.Trim());
        return m.Success ? new EngineVersion(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 0) : null;
    }

    /// <summary>
    /// The engine version recorded near the start of the most recent log in the project's <c>Saved\Logs</c>, if any.
    /// Reads only that one log, and only its first lines.
    /// </summary>
    public static EngineVersion? ReadVersionFromLog(string projectDir)
    {
        try
        {
            var logs = new DirectoryInfo(Path.Combine(projectDir, "Saved", "Logs"));
            if (!logs.Exists) return null;
            var newest = logs.EnumerateFiles("*.log").MaxBy(f => f.LastWriteTimeUtc);
            if (newest is null) return null;

            // The editor keeps its log open while running.
            using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            // The version line follows the startup DLL/plugin messages: around line 500 in a UE 5.8 editor log.
            for (var i = 0; i < MaxLogLines && reader.ReadLine() is { } line; i++)
                if (ParseLogVersion(line) is { } v) return v;
        }
        catch (Exception) { }
        return null;
    }

    private const int MaxLogLines = 3000;

    /// <summary>Parses <c>LogInit: Engine Version: 5.8.4-58902052+++UE5+Release-5.8</c>.</summary>
    internal static EngineVersion? ParseLogVersion(string line)
    {
        var m = LogVersionRegex().Match(line);
        return m.Success
            ? new EngineVersion(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
            : null;
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)$")]
    private static partial Regex AssociationRegex();

    [GeneratedRegex(@"LogInit: Engine Version: (\d+)\.(\d+)\.(\d+)")]
    private static partial Regex LogVersionRegex();
}
