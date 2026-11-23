using System.Text.Json;
using Drs.Unreal.Common.Projects;

namespace Drs.Unreal.Common.Tests;

public class ProjectCacheTests
{
    private static DiscoveredProject D(string path, ProjectSource s = ProjectSource.Recent) => new(path, s);

    [Fact]
    public void Merge_OnlyAdds_NeverRemoves()
    {
        var cache = new ProjectCache();
        Assert.Equal(2, cache.Merge([D(@"D:\A\A.uproject"), D(@"D:\B\B.uproject")]));

        // B dropped off UE's recent list (e.g. engine upgrade): it must stay cached.
        Assert.Equal(0, cache.Merge([D(@"d:\a\A.uproject")]));
        Assert.Equal([@"D:\A\A.uproject", @"D:\B\B.uproject"], cache.Projects.Select(p => p.Path));
    }

    [Fact]
    public void Remove_IgnoresForDiscovery_BrowseUnignores()
    {
        var cache = new ProjectCache();
        cache.Merge([D(@"D:\A\A.uproject")]);
        cache.Remove(@"D:\A\A.uproject");
        Assert.Empty(cache.Projects);

        cache.Merge([D(@"D:\A\A.uproject")]);
        Assert.Empty(cache.Projects);

        cache.AddBrowsed(@"D:\A\A.uproject");
        Assert.Equal(ProjectSource.Browsed, Assert.Single(cache.Projects).Source);
        Assert.Empty(cache.Ignored);
    }

    [Fact]
    public void Validate_DropsMissing_KeepsUnavailable()
    {
        var cache = new ProjectCache();
        cache.Merge([D(@"D:\Here\Here.uproject"), D(@"D:\Gone\Gone.uproject"), D(@"U:\Usb\Usb.uproject")]);

        var result = cache.Validate(p => p switch
        {
            _ when p.Contains("Gone") => ProjectAvailability.Missing,
            _ when p.StartsWith("U:") => ProjectAvailability.Unavailable,
            _ => ProjectAvailability.Available,
        });

        Assert.Equal([@"D:\Here\Here.uproject", @"U:\Usb\Usb.uproject"], cache.Projects.Select(p => p.Path));
        Assert.Equal(ProjectAvailability.Unavailable, result[@"U:\Usb\Usb.uproject"]);
    }

    [Fact]
    public void CheckAvailability_RealFileSystem()
    {
        using var tmp = new TempDir();
        var exists = tmp.File(@"P\P.uproject");
        Assert.Equal(ProjectAvailability.Available, ProjectCache.CheckAvailability(exists));
        Assert.Equal(ProjectAvailability.Missing, ProjectCache.CheckAvailability(Path.Combine(tmp.Root, @"Q\Q.uproject")));

        var unusedDrive = Enumerable.Range('D', 23).Select(c => $"{(char)c}:\\").First(d => !Directory.Exists(d));
        Assert.Equal(ProjectAvailability.Unavailable, ProjectCache.CheckAvailability(unusedDrive + @"X\X.uproject"));
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var cache = new ProjectCache();
        cache.Merge([D(@"D:\A\A.uproject", ProjectSource.SearchFolder)]);
        cache.Remove(@"D:\Z\Z.uproject");
        var back = JsonSerializer.Deserialize<ProjectCache>(JsonSerializer.Serialize(cache))!;
        Assert.Equal(ProjectSource.SearchFolder, Assert.Single(back.Projects).Source);
        Assert.Equal([@"D:\Z\Z.uproject"], back.Ignored);
    }
}
