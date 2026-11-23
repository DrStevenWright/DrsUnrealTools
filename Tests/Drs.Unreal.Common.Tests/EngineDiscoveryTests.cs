using Drs.Unreal.Common.Engines;

namespace Drs.Unreal.Common.Tests;

public class EngineDiscoveryTests
{
    [Fact]
    public void ParsesLauncherInstalledDat_KeepsOnlyEngines()
    {
        const string json = """
        {
            "InstallationList": [
                { "InstallLocation": "C:\\Program Files\\Epic Games\\UE_5.8", "AppName": "UE_5.8" },
                { "InstallLocation": "C:\\Program Files\\Epic Games\\SomeGame", "AppName": "SomeGameApp" },
            ]
        }
        """;
        var result = EngineDiscovery.ParseLauncherInstalledDat(json);
        Assert.Equal([("5.8", @"C:\Program Files\Epic Games\UE_5.8")], result);
    }

    [Fact]
    public void ParsesLauncherInstalledDat_Garbage_ReturnsEmpty() =>
        Assert.Empty(EngineDiscovery.ParseLauncherInstalledDat("not json"));

    [Fact]
    public void ParsesBuildVersion() =>
        Assert.Equal(new EngineVersion(5, 8, 4),
            EngineDiscovery.ParseBuildVersion("""{ "MajorVersion": 5, "MinorVersion": 8, "PatchVersion": 4, "Changelist": 1 }"""));

    [Fact]
    public void Resolve_SkipsEnginesWithoutEditorExe_AndDeduplicatesRoots()
    {
        using var tmp = new TempDir();
        tmp.File(@"Built\Engine\Binaries\Win64\UnrealEditor.exe");
        tmp.File(@"Built\Engine\Build\Build.version", """{ "MajorVersion": 5, "MinorVersion": 8, "PatchVersion": 1 }""");
        tmp.Dir(@"NotBuilt\Engine");
        var built = Path.Combine(tmp.Root, "Built");

        var engines = EngineDiscovery.Resolve(
        [
            ("{GUID-1}", built, EngineSource.Source),
            ("{GUID-2}", Path.Combine(tmp.Root, "NotBuilt"), EngineSource.Source),
            ("5.8", built + "\\", EngineSource.Launcher),   // same root, different spelling
            ("4.0", Path.Combine(tmp.Root, "Missing"), EngineSource.Launcher),
        ]);

        var e = Assert.Single(engines);
        Assert.Equal("{GUID-1}", e.Identifier);
        Assert.Equal(new EngineVersion(5, 8, 1), e.Version);
        Assert.EndsWith("UnrealEditor.exe", e.EditorExe);
    }

    [Fact]
    public void Resolve_FallsBackToUE4EditorExe()
    {
        using var tmp = new TempDir();
        tmp.File(@"UE4\Engine\Binaries\Win64\UE4Editor.exe");
        var e = Assert.Single(EngineDiscovery.Resolve([("4.27", Path.Combine(tmp.Root, "UE4"), EngineSource.Launcher)]));
        Assert.EndsWith("UE4Editor.exe", e.EditorExe);
    }

    [Fact]
    public void ResolveAssociation_MatchesIdentifierStrictly()
    {
        var launcher = new EngineInstall("5.8", @"C:\UE_5.8", EngineSource.Launcher, new(5, 8, 4), "x");
        var source = new EngineInstall("{B6}", @"D:\UnrealEngine", EngineSource.Source, new(5, 8, 4), "x");
        EngineInstall[] engines = [launcher, source];

        Assert.Same(launcher, EngineDiscovery.ResolveAssociation("5.8", @"D:\P\P.uproject", engines));
        Assert.Same(source, EngineDiscovery.ResolveAssociation("{b6}", @"D:\P\P.uproject", engines));
        Assert.Null(EngineDiscovery.ResolveAssociation("5.7", @"D:\P\P.uproject", engines));
    }

    [Fact]
    public void ResolveAssociation_Empty_MeansProjectInsideEngineTree()
    {
        var source = new EngineInstall("{B6}", @"D:\UnrealEngine", EngineSource.Source, null, "x");
        Assert.Same(source, EngineDiscovery.ResolveAssociation("", @"D:\UnrealEngine\Games\G\G.uproject", [source]));
        Assert.Null(EngineDiscovery.ResolveAssociation("", @"D:\Other\G\G.uproject", [source]));
    }
}
