using Drs.Unreal.Common.Ini;

namespace Drs.Unreal.Common.Launch;

/// <summary>
/// Per-user, per-project state that shouldn't churn in source control
/// (for UE Test Tool: <c>&lt;Project&gt;\Saved\UETestTool\User.ini</c>, under UE's already-ignored Saved folder).
/// <code>
/// [State]
/// LastMap=/Game/Maps/DrsTestMap
/// LastConfig=Standalone (noHMD)
/// LastSession=Multiplayer
///
/// [CustomArgs]
/// Standalone (noHMD)=-ResX=1920 -ResY=1080
/// </code>
/// </summary>
public sealed class ProjectUserState
{
    private const string StateSection = "State";
    private const string CustomArgsSection = "CustomArgs";

    public string? LastMap { get; set; }
    public string? LastConfig { get; set; }

    /// <summary>The session last selected, if a session (rather than <see cref="LastConfig"/>) was selected.</summary>
    public string? LastSession { get; set; }

    /// <summary>Engine identifier last used with this project (e.g. <c>5.8</c> or a source build's <c>{GUID}</c>).</summary>
    public string? LastEngine { get; set; }

    /// <summary>Extra arguments per configuration name (case-insensitive).</summary>
    public Dictionary<string, string> CustomArgs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetCustomArgs(string configName) => CustomArgs.GetValueOrDefault(configName, "");

    public static ProjectUserState Load(string path)
    {
        var doc = IniDocument.Load(path);
        var state = new ProjectUserState
        {
            LastMap = NullIfEmpty(doc.Get(StateSection, "LastMap")),
            LastConfig = NullIfEmpty(doc.Get(StateSection, "LastConfig")),
            LastSession = NullIfEmpty(doc.Get(StateSection, "LastSession")),
            LastEngine = NullIfEmpty(doc.Get(StateSection, "LastEngine")),
        };
        foreach (var (key, value) in doc.GetAll(CustomArgsSection))
            if (value.Length > 0) state.CustomArgs[key] = value;
        return state;
    }

    /// <summary>Writes the file, keeping anything else in it (e.g. sections added by future versions).</summary>
    public void Save(string path)
    {
        var doc = IniDocument.Load(path);
        SetOrRemove(doc, StateSection, "LastMap", LastMap);
        SetOrRemove(doc, StateSection, "LastConfig", LastConfig);
        SetOrRemove(doc, StateSection, "LastSession", LastSession);
        SetOrRemove(doc, StateSection, "LastEngine", LastEngine);

        foreach (var (key, _) in doc.GetAll(CustomArgsSection))
            if (!CustomArgs.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) doc.Remove(CustomArgsSection, key);
        foreach (var (key, value) in CustomArgs)
            if (!string.IsNullOrWhiteSpace(value)) doc.Set(CustomArgsSection, key, value.Trim());

        doc.Save(path);
    }

    private static void SetOrRemove(IniDocument doc, string section, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) doc.Remove(section, key);
        else doc.Set(section, key, value);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
