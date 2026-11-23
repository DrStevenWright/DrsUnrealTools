using Drs.Unreal.Common.Projects;

namespace Drs.Unreal.Common.Tests;

public class ProjectDiscoveryTests
{
    [Fact]
    public void ScanFolder_FindsProjectsToDepth_AndSkipsInsideProjects()
    {
        using var tmp = new TempDir();
        var a = tmp.File(@"A\A.uproject");
        tmp.File(@"A\Plugins\Nested\Nested.uproject");        // inside a project: not scanned
        var b = tmp.File(@"Group\B\B.uproject");               // depth 2
        var c = tmp.File(@"Org\Repo\Game\C.uproject");          // depth 3
        tmp.File(@"Deep\W\X\Y\D.uproject");                    // depth 4: too deep
        tmp.File(@"Group\Saved\S\S.uproject");                 // skipped folder

        var found = ProjectDiscovery.ScanFolder(tmp.Root).ToList();
        Assert.Equal(new[] { a, b, c }.Order(), found.Order());
    }

    [Fact]
    public void ScanFolder_FindsProjectsInFoldersNamedLikeProjectSubfolders()
    {
        using var tmp = new TempDir();
        var a = tmp.File(@"Source\Game\Game.uproject");
        var b = tmp.File(@"Content\Demo\Demo.uproject");
        Assert.Equal(new[] { a, b }.Order(), ProjectDiscovery.ScanFolder(tmp.Root).Order());
    }

    [Fact]
    public void ScanFolder_DoesNotEnterEngineTrees()
    {
        using var tmp = new TempDir();
        tmp.File(@"UnrealEngine\Engine\Build\Build.version", "{}");
        tmp.File(@"UnrealEngine\Templates\TP_Blank.uproject");
        tmp.File(@"UnrealEngine\Samples\S\S.uproject");
        var mine = tmp.File(@"Mine\Mine.uproject");
        Assert.Equal([mine], ProjectDiscovery.ScanFolder(tmp.Root));
    }

    [Fact]
    public void ScanFolder_SurvivesJunctionLoops_AndFollowsJunctions()
    {
        using var tmp = new TempDir();
        var real = tmp.File(@"Elsewhere\P\P.uproject");
        var scanRoot = tmp.Dir("Root");
        Junction.Create(Path.Combine(scanRoot, "Loop"), scanRoot);
        Junction.Create(Path.Combine(scanRoot, "Linked"), Path.Combine(tmp.Root, "Elsewhere"));

        var found = ProjectDiscovery.ScanFolder(scanRoot, depth: 5).ToList();
        Assert.Equal([Path.Combine(scanRoot, @"Linked\P\P.uproject")], found);
        Assert.True(File.Exists(real));
    }

    [Fact]
    public void Discover_SearchesOnlyTheGivenFolders_ExcludesEngineTemplates()
    {
        using var tmp = new TempDir();
        var a = tmp.File(@"Repos\A\A.uproject");
        var b = tmp.File(@"Unreal Projects\B\B.uproject");
        tmp.File(@"Elsewhere\C\C.uproject");                                    // not a search folder
        tmp.File(@"Repos\UnrealEngine\Engine\Build\Build.version", "{}");
        tmp.File(@"Repos\UnrealEngine\Templates\TP\TP.uproject");             // inside an engine tree

        var result = ProjectDiscovery.Discover(
            searchFolders: [Path.Combine(tmp.Root, "Repos"), Path.Combine(tmp.Root, "Unreal Projects")],
            engineRoots: [Path.Combine(tmp.Root, @"Repos\UnrealEngine")]);

        Assert.Equal([(a, ProjectSource.SearchFolder), (b, ProjectSource.SearchFolder)], result.Select(r => (r.UProjectPath, r.Source)));
        Assert.Empty(ProjectDiscovery.Discover([], []));
    }

    [Fact]
    public void ReadsEngineAssociation()
    {
        using var tmp = new TempDir();
        var path = tmp.File(@"P\P.uproject", """
            {
                // comment
                "FileVersion": 3,
                "EngineAssociation": "{B68221B9-458C-5ECE-5174-ACB6E6A197BB}",
            }
            """);
        var p = UProjectFile.Read(path);
        Assert.Equal("{B68221B9-458C-5ECE-5174-ACB6E6A197BB}", p.EngineAssociation);
        Assert.Equal("P", p.Name);
    }
}
