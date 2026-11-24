using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Drs.Unreal.Common.Launch;
using UETestTool.Services;

namespace UETestTool;

/// <summary>How many instances of one client configuration a session launches.</summary>
public sealed partial class ClientCount(LaunchConfig config, int count) : ObservableObject
{
    public LaunchConfig Config { get; } = config;
    public string Name => Config.Name;
    public string Args => Config.Args;

    [ObservableProperty] private int _count = count;
}

/// <summary>
/// Creates or edits a session: a name, one server configuration and a number of each client configuration.
/// Saving gives the server followed by the clients, in the order the configurations are listed.
/// </summary>
public partial class SessionWindow : Window
{
    public const int MaxPerClient = 8;

    private readonly List<ClientCount> _clients;

    public SessionEdit? Result { get; private set; }

    public SessionWindow(LaunchSession? session, IReadOnlyList<LaunchConfig> configs)
    {
        InitializeComponent();
        Title = session is null ? "New session" : $"Edit session: {session.Name}";

        var entries = session?.Entries ?? [LaunchConfig.DedicatedServer, LaunchConfig.Client, LaunchConfig.Client];
        bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // The client list first: setting the server raises Changed, which reads it.
        _clients = configs.Where(c => c.IsClient)
            .Select(c => new ClientCount(c, Math.Min(MaxPerClient, entries.Count(e => Same(e, c.Name)))))
            .ToList();
        foreach (var c in _clients) c.PropertyChanged += (_, _) => Changed(this, new RoutedEventArgs());
        ClientList.ItemsSource = _clients;

        var servers = configs.Where(c => c.IsServer).ToList();
        ServerBox.ItemsSource = servers;
        ServerBox.SelectedItem = servers.FirstOrDefault(s => entries.Any(e => Same(e, s.Name))) ?? servers.FirstOrDefault();

        NameBox.Text = session?.Name ?? "";
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
        UpdateSummary();
    }

    private IReadOnlyList<string> Entries =>
        ServerBox.SelectedItem is LaunchConfig server
            ? [server.Name, .. _clients.SelectMany(c => Enumerable.Repeat(c.Name, c.Count))]
            : [];

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || SummaryText is null) return;
        ErrorText.Visibility = Visibility.Collapsed;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var clients = _clients.Sum(c => c.Count);
        SummaryText.Text = ServerBox.SelectedItem is null
            ? "This project has no server configurations."
            : $"Launches {LaunchSession.Describe(Entries)}: {clients + 1} instance(s).";
    }

    private void Minus_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClientCount c && c.Count > 0) c.Count--;
    }

    private void Plus_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClientCount c && c.Count < MaxPerClient) c.Count++;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var error = LaunchConfig.ValidateName(name)
                    ?? (ServerBox.SelectedItem is null ? "Choose a server." : null)
                    ?? (_clients.Sum(c => c.Count) == 0 ? "Add at least one client." : null);
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Result = new SessionEdit(name, Entries);
        DialogResult = true;
    }
}
