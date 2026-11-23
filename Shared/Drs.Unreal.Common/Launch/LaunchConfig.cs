namespace Drs.Unreal.Common.Launch;

/// <summary>
/// The hidden part of a configuration: what gets injected before the visible arguments.
/// The user never sees or types the project path or map; these rules decide how they are added.
/// </summary>
/// <param name="UsesMap">Inject the selected map (false for a client, which connects instead).</param>
/// <param name="MapOptions">URL options appended to the map, e.g. <c>?listen</c>.</param>
/// <param name="Connect">Address injected instead of a map, e.g. <c>127.0.0.1</c>.</param>
public sealed record LaunchRule(bool UsesMap, string MapOptions = "", string? Connect = null)
{
    public static readonly LaunchRule WithMap = new(true);
}

/// <summary>A selectable launch configuration.</summary>
/// <param name="Name">Display name, unique (case-insensitive).</param>
/// <param name="Base">The built-in configuration this one derives its <see cref="Rule"/> from.</param>
/// <param name="Args">The visible, editable command-line arguments.</param>
/// <param name="IsBuiltIn">Shipped with the app (not from the project's .ini).</param>
public sealed record LaunchConfig(string Name, string Base, LaunchRule Rule, string Args, bool IsBuiltIn)
{
    public override string ToString() => Name;

    public const string Standalone = "Standalone";
    public const string ListenServer = "Listen Server";
    public const string DedicatedServer = "Dedicated Server";
    public const string Client = "Client";

    /// <summary>The defaults every project gets. A project .ini section with the same name overrides one.</summary>
    public static IReadOnlyList<LaunchConfig> BuiltIns { get; } =
    [
        new(Standalone, Standalone, LaunchRule.WithMap, "-game -log", true),
        new(ListenServer, ListenServer, new LaunchRule(true, "?listen"), "-game -log", true),
        new(DedicatedServer, DedicatedServer, LaunchRule.WithMap, "-server -log", true),
        new(Client, Client, new LaunchRule(false, "", "127.0.0.1"), "-game -log", true),
    ];

    public static LaunchConfig? FindBuiltIn(string name) =>
        BuiltIns.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns an error message if <paramref name="name"/> can't be used as a configuration name, else null.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a name.";
        if (name != name.Trim()) return "The name can't start or end with spaces.";
        if (name.IndexOfAny(['[', ']', '=', '\r', '\n']) >= 0) return "The name can't contain [ ] or =.";
        if (name[0] is ';' or '#') return "The name can't start with ; or #.";
        return null;
    }
}
