using System.Text.Json;
using Microsoft.Win32;

namespace Drs.Unreal.Common.Engines;

/// <summary>
/// Finds installed Unreal Engines the same way the engine's own FDesktopPlatformModule does,
/// without needing an external locator tool:
/// <list type="bullet">
/// <item>Source builds: <c>HKCU\Software\Epic Games\Unreal Engine\Builds</c> (GUID → root).</item>
/// <item>Launcher installs: <c>%ProgramData%\Epic\UnrealEngineLauncher\LauncherInstalled.dat</c>.</item>
/// <item>Legacy launcher installs: <c>HKLM\SOFTWARE\EpicGames\Unreal Engine\&lt;ver&gt;\InstalledDirectory</c>.</item>
/// </list>
/// An engine is only returned if its editor executable exists (a registered source build
/// may not have been compiled, and registry entries can outlive an uninstall).
/// </summary>
public static class EngineDiscovery
{
    internal const string SourceBuildsKey = @"Software\Epic Games\Unreal Engine\Builds";
    internal const string LegacyLauncherKey = @"SOFTWARE\EpicGames\Unreal Engine";

    public static string LauncherInstalledDatPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        @"Epic\UnrealEngineLauncher\LauncherInstalled.dat");

    public static IReadOnlyList<EngineInstall> Discover()
    {
        var candidates = new List<(string Id, string Root, EngineSource Source)>();
        candidates.AddRange(ReadSourceBuilds().Select(c => (c.Id, c.Root, EngineSource.Source)));
        candidates.AddRange(ReadLauncherInstalledDat(LauncherInstalledDatPath).Select(c => (c.Id, c.Root, EngineSource.Launcher)));
        candidates.AddRange(ReadLegacyLauncherRegistry().Select(c => (c.Id, c.Root, EngineSource.Launcher)));
        return Resolve(candidates);
    }

    /// <summary>Turns raw (identifier, root) candidates into usable installs, de-duplicated by root.</summary>
    internal static IReadOnlyList<EngineInstall> Resolve(IEnumerable<(string Id, string Root, EngineSource Source)> candidates)
    {
        var result = new List<EngineInstall>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, rawRoot, source) in candidates)
        {
            if (string.IsNullOrWhiteSpace(rawRoot)) continue;

            string root;
            try { root = PathUtil.Normalize(rawRoot); }
            catch (Exception) { continue; }

            if (!seenRoots.Add(root)) continue;

            var editor = FindEditorExe(root);
            if (editor is null) continue;

            result.Add(new EngineInstall(id, root, source, ReadBuildVersion(root), editor));
        }

        return result
            .OrderByDescending(e => e.Version?.Major ?? 0)
            .ThenByDescending(e => e.Version?.Minor ?? 0)
            .ThenByDescending(e => e.Version?.Patch ?? 0)
            .ThenBy(e => e.Source)
            .ToList();
    }

    public static string? FindEditorExe(string engineRoot)
    {
        foreach (var name in new[] { "UnrealEditor.exe", "UE4Editor.exe" })
        {
            var path = Path.Combine(engineRoot, "Engine", "Binaries", "Win64", name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static EngineVersion? ReadBuildVersion(string engineRoot)
    {
        var path = Path.Combine(engineRoot, "Engine", "Build", "Build.version");
        try
        {
            if (!File.Exists(path)) return null;
            return ParseBuildVersion(File.ReadAllText(path));
        }
        catch (Exception) { return null; }
    }

    internal static EngineVersion? ParseBuildVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, Json.LenientOptions);
            var root = doc.RootElement;
            return new EngineVersion(
                root.GetProperty("MajorVersion").GetInt32(),
                root.GetProperty("MinorVersion").GetInt32(),
                root.TryGetProperty("PatchVersion", out var p) ? p.GetInt32() : 0);
        }
        catch (Exception) { return null; }
    }

    internal static IEnumerable<(string Id, string Root)> ReadSourceBuilds()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SourceBuildsKey);
        if (key is null) yield break;
        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string root) yield return (name, root);
        }
    }

    internal static IEnumerable<(string Id, string Root)> ReadLegacyLauncherRegistry()
    {
        using var key = Registry.LocalMachine.OpenSubKey(LegacyLauncherKey);
        if (key is null) yield break;
        foreach (var version in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(version);
            if (sub?.GetValue("InstalledDirectory") is string root) yield return (version, root);
        }
    }

    internal static IEnumerable<(string Id, string Root)> ReadLauncherInstalledDat(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path)) return [];
            text = File.ReadAllText(path);
        }
        catch (Exception) { return []; }
        return ParseLauncherInstalledDat(text);
    }

    /// <summary>Engine entries have AppName <c>UE_x.y</c>; their identifier is <c>x.y</c>.</summary>
    internal static IReadOnlyList<(string Id, string Root)> ParseLauncherInstalledDat(string json)
    {
        var result = new List<(string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(json, Json.LenientOptions);
            if (!doc.RootElement.TryGetProperty("InstallationList", out var list)) return result;
            foreach (var item in list.EnumerateArray())
            {
                var appName = item.TryGetProperty("AppName", out var a) ? a.GetString() : null;
                var location = item.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                if (appName is null || location is null) continue;
                if (!appName.StartsWith("UE_", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add((appName[3..], location));
            }
        }
        catch (Exception) { }
        return result;
    }

    /// <summary>
    /// Finds the engine a project's <c>EngineAssociation</c> refers to. Returns null if none matches.
    /// An empty association means the project lives inside an engine tree (a "foreign" vs "native" project);
    /// in that case the engine whose root contains the project is returned.
    /// </summary>
    public static EngineInstall? ResolveAssociation(string? association, string uprojectPath, IEnumerable<EngineInstall> engines)
    {
        var list = engines as IReadOnlyCollection<EngineInstall> ?? engines.ToList();
        if (string.IsNullOrWhiteSpace(association))
        {
            var dir = Path.GetFullPath(Path.GetDirectoryName(uprojectPath) ?? "");
            return list.FirstOrDefault(e => PathUtil.IsUnder(dir, e.RootDir));
        }

        // Strict match, as UE itself does: "5.8" is the launcher install, "{GUID}" a specific source build.
        return list.FirstOrDefault(e => string.Equals(e.Identifier, association, StringComparison.OrdinalIgnoreCase));
    }
}
