using Drs.Unreal.Common.Launch;

namespace Drs.Unreal.Common.Tests;

public class LaunchCommandTests
{
    private const string Editor = @"C:\Program Files\Epic Games\UE_5.8\Engine\Binaries\Win64\UnrealEditor.exe";
    private const string Project = @"D:\Repos\My Project\My.uproject";

    private static LaunchConfig BuiltIn(string name) => LaunchConfig.FindBuiltIn(name)!;

    private static string Args(LaunchConfig config, string? map, string? custom = null) =>
        LaunchCommandBuilder.Build(Editor, Project, config, map, custom, out _)!.Arguments;

    [Fact]
    public void Standalone() =>
        Assert.Equal("\"D:\\Repos\\My Project\\My.uproject\" /Game/Maps/Test -game -log",
            Args(BuiltIn(LaunchConfig.Standalone), "/Game/Maps/Test"));

    [Fact]
    public void ListenServer_AppendsListenToMap() =>
        Assert.Equal("\"D:\\Repos\\My Project\\My.uproject\" /Game/Maps/Test?listen -game -log",
            Args(BuiltIn(LaunchConfig.ListenServer), "/Game/Maps/Test"));

    [Fact]
    public void DedicatedServer() =>
        Assert.Equal("\"D:\\Repos\\My Project\\My.uproject\" /Game/Maps/Test -server -log",
            Args(BuiltIn(LaunchConfig.DedicatedServer), "/Game/Maps/Test"));

    [Fact]
    public void Client_ConnectsInsteadOfMap_AndAddsCustomArgs() =>
        Assert.Equal("\"D:\\Repos\\My Project\\My.uproject\" 127.0.0.1 -game -log -noHMD -ResX=1200",
            Args(BuiltIn(LaunchConfig.Client), "/Game/Maps/Test", "  -noHMD -ResX=1200 "));

    [Fact]
    public void Standalone_WithoutMap_UsesProjectDefault() =>
        Assert.Equal("\"D:\\Repos\\My Project\\My.uproject\" -game -log", Args(BuiltIn(LaunchConfig.Standalone), null));

    [Fact]
    public void ListenServer_WithoutMap_IsAnError()
    {
        var cmd = LaunchCommandBuilder.Build(Editor, Project, BuiltIn(LaunchConfig.ListenServer), "", null, out var error);
        Assert.Null(cmd);
        Assert.Contains("needs a map", error);
    }

    [Fact]
    public void MapWithSpaces_IsQuoted() =>
        Assert.Contains("\"/Game/My Maps/Test?listen\"", Args(BuiltIn(LaunchConfig.ListenServer), "/Game/My Maps/Test"));

    [Fact]
    public void DisplayText_QuotesEditorPath_WorkingDirIsBinaries()
    {
        var cmd = LaunchCommandBuilder.Build(Editor, Project, BuiltIn(LaunchConfig.Standalone), null, null, out _)!;
        Assert.StartsWith($"\"{Editor}\" \"", cmd.DisplayText);
        Assert.Equal(Path.GetDirectoryName(Editor), cmd.WorkingDirectory);
    }
}
