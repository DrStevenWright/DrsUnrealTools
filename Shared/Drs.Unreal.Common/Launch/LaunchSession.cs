namespace Drs.Unreal.Common.Launch;

/// <summary>
/// A multiplayer session: a named list of configuration names, launched together. The one server in the list starts
/// first; the clients start once it is listening.
/// </summary>
/// <param name="Name">Display name, unique among sessions (case-insensitive).</param>
/// <param name="Entries">Configuration names, one per instance (so a name appears twice for two clients).</param>
/// <param name="IsBuiltIn">Shipped with the app (not from the project's .ini).</param>
public sealed record LaunchSession(string Name, IReadOnlyList<string> Entries, bool IsBuiltIn)
{
    public override string ToString() => Name;

    public const string Multiplayer = "Multiplayer";

    /// <summary>The sessions every project gets. A project .ini section with the same name overrides one.</summary>
    public static IReadOnlyList<LaunchSession> BuiltIns { get; } =
    [
        new(Multiplayer, [LaunchConfig.DedicatedServer, LaunchConfig.Client, LaunchConfig.Client], true),
    ];

    public static LaunchSession? FindBuiltIn(string name) =>
        BuiltIns.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>What the session launches, in words, e.g. <c>Dedicated Server, Client ×2, Client (noHMD)</c>.</summary>
    public string Description => Describe(Entries);

    public static string Describe(IEnumerable<string> entries) =>
        string.Join(", ", entries
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ×{g.Count()}"));

    /// <summary>Works out which configuration is the server and which are clients, or why the session can't launch.</summary>
    public SessionPlan Resolve(IReadOnlyList<LaunchConfig> configs)
    {
        if (Entries.Count == 0) return SessionPlan.Invalid("It doesn't list any configurations.");

        LaunchConfig? server = null;
        var clients = new List<LaunchConfig>();
        foreach (var entry in Entries)
        {
            var config = configs.FirstOrDefault(c => string.Equals(c.Name, entry, StringComparison.OrdinalIgnoreCase));
            if (config is null) return SessionPlan.Invalid($"'{entry}' isn't one of this project's configurations.");
            if (config.IsServer)
            {
                if (server is not null) return SessionPlan.Invalid("It has more than one server.");
                server = config;
            }
            else if (config.IsClient) clients.Add(config);
            else return SessionPlan.Invalid($"'{config.Name}' doesn't connect to a server, so it can't be part of a session.");
        }
        if (server is null) return SessionPlan.Invalid("It needs a Dedicated Server or Listen Server.");
        return new SessionPlan(server, clients, null);
    }
}

/// <summary>A resolved session: the server and clients to launch, or the <see cref="Error"/> that stops it.</summary>
public sealed record SessionPlan(LaunchConfig? Server, IReadOnlyList<LaunchConfig> Clients, string? Error)
{
    internal static SessionPlan Invalid(string error) => new(null, [], error);

    /// <summary>
    /// The client's configuration with the server's port added to a local connect address (<c>127.0.0.1</c> becomes
    /// <c>127.0.0.1:7778</c>), so a server started with <c>-port=</c> is still found. Other addresses are left alone.
    /// </summary>
    public static LaunchConfig ConnectingTo(LaunchConfig client, int port)
    {
        var connect = client.Rule.Connect?.Trim();
        if (connect is not ("127.0.0.1" or "localhost")) return client;
        return client with { Rule = client.Rule with { Connect = $"{connect}:{port}" } };
    }
}
