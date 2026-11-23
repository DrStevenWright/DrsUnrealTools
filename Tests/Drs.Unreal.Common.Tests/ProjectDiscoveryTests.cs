using Drs.Unreal.Common.Projects;

namespace Drs.Unreal.Common.Tests;

public class ProjectDiscoveryTests
{
    [Fact]
    public void ParsesEditorSettings_StructAndPlainForms()
    {
        const string ini = """
            [/Script/UnrealEd.EditorSettings]
            CreatedProjectPaths=D:\Unreal Projects
            CreatedProjectPaths=D:/
            RecentlyOpenedProjectFiles=(ProjectName="D:/Repos/A/A.uproject",LastOpenTime=2026.11.23-11.11.00)
            +RecentlyOpenedProjectFiles=D:/Old/B/B.uproject
            RecentlyOpenedProjectFilesExtra=ignored
            """;
        var info = ProjectDiscovery.ParseEditorSettings(ini.Replace("\n", "\r\n"));
        Assert.Equal(["D:/Repos/A/A.uproject", "D:/Old/B/B.uproject"], info.RecentProjects);
        Assert.Equal([@"D:\Unreal Projects", "D:/"], info.CreatedProjectPaths);
    }

    [Fact]
    public void ScanFolder_FindsProjectsToDepth_AndSkipsInsideProjects()
    {
        using var tmp = new TempDir();
        var a = tmp.File(@"A\A.uproject");
        tmp.File(@"A\Plugins\Nested\Nested.uproject");        // inside a project: not scanned
        var b = tmp.File(@"Group\B\B.uproject");               // depth 2
        tmp.File(@"Deep\X\Y\C\C.uproject");                    // depth 3: too deep
        tmp.File(@"Group\Saved\S\S.uproject");                 // skipped folder

        var found = ProjectDiscovery.ScanFolder(tmp.Root).ToList();
        Assert.Equal([a, b], found.Order());
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
    public void Discover_CombinesSources_ExcludesEngineTemplates()
    {
        using var tmp = new TempDir();
        var recent = tmp.File(@"Projects\Recent\Recent.uproject");
        var created = tmp.File(@"Created\New\New.uproject");
        var searched = tmp.File(@"Search\Found\Found.uproject");
        var template = tmp.File(@"Engine58\Templates\TP\TP.uproject");
        var settings = tmp.File(@"LocalUE\5.8\Saved\Config\WindowsEditor\EditorSettings.ini", $"""
            CreatedProjectPaths={Path.Combine(tmp.Root, "Created")}
            RecentlyOpenedProjectFiles=(ProjectName="{recent.Replace('\\', '/')}",LastOpenTime=x)
            RecentlyOpenedProjectFiles=(ProjectName="{template}",LastOpenTime=x)
            RecentlyOpenedProjectFiles=(ProjectName="Z:/Gone/Gone.uproject",LastOpenTime=x)
            """);

        var result = ProjectDiscovery.Discover(
            searchFolders: [Path.Combine(tmp.Root, "Search")],
            engineRoots: [Path.Combine(tmp.Root, "Engine58")],
            unrealLocalAppData: Path.Combine(tmp.Root, "LocalUE"));

        Assert.Equal(
            new[] { (recent, ProjectSource.Recent), (created, ProjectSource.Recent), (searched, ProjectSource.SearchFolder) },
            result.Select(r => (r.UProjectPath, r.Source)));
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
