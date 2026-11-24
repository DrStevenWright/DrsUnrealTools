using System.Windows;
using System.Windows.Controls;
using Drs.Unreal.Common.Launch;
using UETestTool.Services;

namespace UETestTool;

/// <summary>
/// Creates a configuration from a template, or edits one. When editing, the configuration is its own template, so it
/// keeps how it launches (map, <c>?listen</c>, connect address); saving under a new name makes a copy.
/// </summary>
public partial class ConfigWindow : Window
{
    private readonly LaunchConfig? _editing;

    /// <summary>The arguments last filled in from a template; replaced when the template changes, unless edited.</summary>
    private string _prefilledArgs = "";

    public ConfigEdit? Result { get; private set; }

    public ConfigWindow(LaunchConfig? config, IReadOnlyList<LaunchConfig> configs, LaunchConfig? suggestedTemplate)
    {
        InitializeComponent();
        _editing = config;

        if (config is null)
        {
            Title = "New configuration";
            TemplateBox.ItemsSource = configs;
            TemplateBox.SelectedItem = suggestedTemplate is null
                ? configs.FirstOrDefault()
                : configs.FirstOrDefault(c => c.Name == suggestedTemplate.Name) ?? configs.FirstOrDefault();
        }
        else
        {
            Title = $"Edit configuration: {config.Name}";
            TemplateLabel.Text = "Based on";
            TemplateBox.Visibility = Visibility.Collapsed;
            BasedOnText.Visibility = Visibility.Visible;
            BasedOnText.Text = config.IsBuiltIn ? "Built-in configuration" : config.Base;
            TemplateHint.Text = Describe(config) +
                                (config.IsBuiltIn ? " Saving under the same name overrides the built-in for this project." : "") +
                                " Save under a new name to make a copy.";
            NameBox.Text = config.Name;
            ArgsBox.Text = config.Args;
        }

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private LaunchConfig? ChosenTemplate => _editing ?? TemplateBox.SelectedItem as LaunchConfig;

    private static string Describe(LaunchConfig c)
    {
        var r = c.Rule;
        var what = !string.IsNullOrEmpty(r.Connect) ? $"Connects to {r.Connect}."
            : r.UsesMap ? (r.MapOptions.Length > 0 ? $"Opens the selected map with {r.MapOptions}." : "Opens the selected map.")
            : "Doesn't open a map.";
        return what;
    }

    private void Template_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not LaunchConfig template) return;
        TemplateHint.Text = Describe(template) + " The new configuration launches the same way and starts with its arguments.";
        if (ArgsBox.Text == _prefilledArgs) ArgsBox.Text = template.Args;   // not edited yet: follow the template
        _prefilledArgs = template.Args;
    }

    private void Changed(object sender, TextChangedEventArgs e)
    {
        if (ErrorText is not null) ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var error = LaunchConfig.ValidateName(name) ?? (ChosenTemplate is null ? "Choose a template." : null);
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Result = new ConfigEdit(name, ChosenTemplate!, ArgsBox.Text.Trim());
        DialogResult = true;
    }
}
