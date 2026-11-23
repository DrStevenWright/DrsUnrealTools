using CommunityToolkit.Mvvm.ComponentModel;
using Drs.Unreal.Common.Launch;
using Drs.Unreal.Common.Projects;

namespace UETestTool.ViewModels;

public sealed class ProjectItem(UProjectFile file, ProjectAvailability availability)
{
    public UProjectFile File { get; } = file;
    public ProjectAvailability Availability { get; } = availability;

    public string Name => File.Name;
    public string Path => File.Path;
    public string Directory => File.Directory;
    public bool IsAvailable => Availability == ProjectAvailability.Available;
    public string Detail => IsAvailable ? Directory : $"{Directory}  (unavailable)";

    public override string ToString() => Name;
}

public sealed partial class ConfigItem : ObservableObject
{
    private readonly Action<ConfigItem> _customArgsChanged;

    public ConfigItem(LaunchConfig config, string customArgs, bool isProjectDefined, Action<ConfigItem> customArgsChanged)
    {
        Config = config;
        _customArgs = customArgs;
        IsProjectDefined = isProjectDefined;
        _customArgsChanged = customArgsChanged;
    }

    public LaunchConfig Config { get; }
    public string Name => Config.Name;
    public string Args => Config.Args;

    /// <summary>Defined in the project's UETestTool.ini (so it can be deleted there).</summary>
    public bool IsProjectDefined { get; }

    /// <summary>What gets injected before the arguments, in words (for the tooltip).</summary>
    public string RuleDescription
    {
        get
        {
            var r = Config.Rule;
            var what = r.UsesMap ? (r.MapOptions.Length > 0 ? $"Opens the selected map with {r.MapOptions}" : "Opens the selected map") : "No map";
            if (!string.IsNullOrEmpty(r.Connect)) what += $", connects to {r.Connect}";
            var origin = IsProjectDefined
                ? (LaunchConfig.FindBuiltIn(Name) is not null ? "Overrides the built-in; defined in Config/UETestTool.ini" : $"Based on {Config.Base}; defined in Config/UETestTool.ini")
                : "Built-in";
            return $"{what}.\n{origin}.\n\n{Args}";
        }
    }

    [ObservableProperty]
    private string _customArgs;

    partial void OnCustomArgsChanged(string value) => _customArgsChanged(this);

    public override string ToString() => Name;
}
