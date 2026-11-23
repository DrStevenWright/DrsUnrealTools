using Drs.Unreal.Common.Maps;

namespace Drs.Unreal.Common.Tests;

public class MapDiscoveryTests
{
    [Fact]
    public void FindsProjectAndContentPluginMaps_WithPackagePaths()
    {
        using var tmp = new TempDir();
        tmp.File(@"Content\Maps\Main.umap");
        tmp.File(@"Content\Developers\me\Scratch.umap");
        tmp.File(@"Content\__ExternalActors__\Maps\Main\A\B.umap");   // never real maps; skipped
        tmp.File(@"Content\Maps\NotAMap.uasset");
        tmp.File(@"Plugins\Category\ContentPlugin\ContentPlugin.uplugin", """{ "CanContainContent": true, }""");
        tmp.File(@"Plugins\Category\ContentPlugin\Content\Test\PluginMap.umap");
        tmp.File(@"Plugins\CodeOnly\CodeOnly.uplugin", """{ "CanContainContent": false }""");
        tmp.File(@"Plugins\CodeOnly\Content\Hidden.umap");

        var maps = MapDiscovery.Discover(tmp.Root);

        Assert.Equal(
            ["/Game/Developers/me/Scratch", "/Game/Maps/Main", "/ContentPlugin/Test/PluginMap"],
            maps.Select(m => m.PackagePath));
        Assert.Equal("ContentPlugin", maps[2].PluginName);
        Assert.Null(maps[0].PluginName);
    }

    [Fact]
    public void ContentJunctionLoop_Terminates()
    {
        using var tmp = new TempDir();
        tmp.File(@"Content\Maps\Main.umap");
        Junction.Create(Path.Combine(tmp.Root, @"Content\Maps\Loop"), Path.Combine(tmp.Root, "Content"));
        Assert.Equal(["/Game/Maps/Main"], MapDiscovery.Discover(tmp.Root).Select(m => m.PackagePath));
    }

    [Fact]
    public void NoContentFolder_ReturnsEmpty()
    {
        using var tmp = new TempDir();
        Assert.Empty(MapDiscovery.Discover(tmp.Root));
    }
}
