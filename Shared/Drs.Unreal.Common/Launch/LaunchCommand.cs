namespace Drs.Unreal.Common.Launch;

/// <summary>A fully-resolved editor launch: what to run and with which (raw) argument string.</summary>
/// <param name="Arguments">
/// One raw string, passed to the editor unchanged. UE parses its own command line, so user-typed arguments
/// (including any quoting they contain) are not split or re-quoted.
/// </param>
public sealed record LaunchCommand(string EditorExe, string Arguments, string WorkingDirectory)
{
    /// <summary>The whole command as it would be typed in a console (for "Show command" / logging).</summary>
    public string DisplayText => $"{Quote(EditorExe)} {Arguments}";

    internal static string Quote(string s) => s.Contains(' ') || s.Contains('\t') || s.Length == 0 ? $"\"{s}\"" : s;
}

public static class LaunchCommandBuilder
{
    /// <summary>
    /// Builds <c>"&lt;Editor&gt;" "&lt;Project&gt;.uproject" &lt;Map&gt;&lt;MapOptions&gt; | &lt;Connect&gt; &lt;Args&gt; &lt;CustomArgs&gt;</c>.
    /// </summary>
    /// <param name="mapPackagePath">e.g. <c>/Game/Maps/Test</c>; null to let the project use its default map.</param>
    /// <param name="error">Why the launch can't be built (shown to the user), or null.</param>
    public static LaunchCommand? Build(
        string editorExe, string uprojectPath, LaunchConfig config, string? mapPackagePath, string? customArgs,
        out string? error)
    {
        error = null;
        var parts = new List<string> { LaunchCommand.Quote(uprojectPath) };
        var rule = config.Rule;

        if (rule.UsesMap)
        {
            if (!string.IsNullOrWhiteSpace(mapPackagePath))
                parts.Add(LaunchCommand.Quote(mapPackagePath.Trim() + rule.MapOptions));
            else if (!string.IsNullOrEmpty(rule.MapOptions))
            {
                error = $"'{config.Name}' needs a map (it adds {rule.MapOptions}). Select a map.";
                return null;
            }
            // else: no map selected, UE loads the project's default map.
        }
        if (!string.IsNullOrWhiteSpace(rule.Connect)) parts.Add(rule.Connect.Trim());

        if (!string.IsNullOrWhiteSpace(config.Args)) parts.Add(config.Args.Trim());
        if (!string.IsNullOrWhiteSpace(customArgs)) parts.Add(customArgs.Trim());

        var workingDir = Path.GetDirectoryName(editorExe) ?? "";
        return new LaunchCommand(editorExe, string.Join(' ', parts), workingDir);
    }
}
