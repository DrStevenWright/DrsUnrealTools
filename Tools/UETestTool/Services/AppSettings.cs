using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Drs.Unreal.Common.Projects;

namespace UETestTool.Services;

/// <summary>
/// Per-machine settings: <c>%LOCALAPPDATA%\DrsUnrealTools\UETestTool\settings.json</c>.
/// Per-project state lives in the project instead (see <see cref="ProjectFiles"/>).
/// </summary>
public sealed class AppSettings
{
    public string? LastProject { get; set; }

    /// <summary>Engine identifier, used when the selected project has no remembered engine of its own.</summary>
    public string? LastEngine { get; set; }

    /// <summary>Extra folders scanned for projects (shallowly) on startup and Refresh.</summary>
    public List<string> SearchFolders { get; set; } = [];

    public ProjectCache ProjectCache { get; set; } = new();

    public WindowPlacement? Window { get; set; }

    public sealed class WindowPlacement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    // ---- Persistence ----------------------------------------------------------------------------

    /// <summary>
    /// The settings file. The <c>UETESTTOOL_SETTINGS</c> environment variable overrides it, so the app can be
    /// exercised against a throwaway settings file without touching the real one.
    /// </summary>
    public static string DefaultPath =>
        Environment.GetEnvironmentVariable("UETESTTOOL_SETTINGS") is { Length: > 0 } overridePath
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DrsUnrealTools", "UETestTool", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Enums as names: readable, and survives reordering the enum.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads settings; a missing or corrupt file gives defaults (a corrupt one is kept as .bad for inspection).</summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            try { File.Copy(path, path + ".bad", overwrite: true); } catch { /* best effort */ }
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception)
        {
            // Settings are a convenience; never crash the app over them.
        }
    }
}

/// <summary>Where UE Test Tool keeps its files inside a project.</summary>
public static class ProjectFiles
{
    /// <summary>Shared configurations, meant to be committed.</summary>
    public static string SharedConfig(string projectDir) => Path.Combine(projectDir, "Config", "UETestTool.ini");

    /// <summary>Per-user state, under UE's (already git-ignored) Saved folder.</summary>
    public static string UserState(string projectDir) => Path.Combine(projectDir, "Saved", "UETestTool", "User.ini");
}
