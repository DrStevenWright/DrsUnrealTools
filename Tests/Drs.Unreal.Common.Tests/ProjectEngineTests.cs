using Drs.Unreal.Common.Engines;

namespace Drs.Unreal.Common.Tests;

public class ProjectEngineTests
{
    private static readonly EngineInstall Launcher58 = new("5.8", @"C:\UE_5.8", EngineSource.Launcher, new(5, 8, 4), "x");
    private static readonly EngineInstall Source58 = new("{B6}", @"D:\UnrealEngine", EngineSource.Source, new(5, 8, 1), "x");
    private static readonly EngineInstall Launcher59 = new("5.9", @"C:\UE_5.9", EngineSource.Launcher, new(5, 9, 0), "x");
    private const string Project = @"D:\P\P.uproject";

    private static ProjectEngineInfo Identify(string? association, EngineInstall[] engines, EngineVersion? logVersion = null) =>
        ProjectEngine.Identify(association, Project, engines, _ => logVersion);

    [Theory]
    [InlineData("5.8", 5, 8)]
    [InlineData("4.27", 4, 27)]
    public void ParsesVersionAssociations(string association, int major, int minor) =>
        Assert.Equal(new EngineVersion(major, minor, 0), ProjectEngine.ParseAssociationVersion(association));

    [Theory]
    [InlineData("{B6}")]
    [InlineData("5")]
    [InlineData("five.eight")]
    [InlineData("5.8.4")]
    public void OtherAssociations_AreNotVersions(string association) =>
        Assert.Null(ProjectEngine.ParseAssociationVersion(association));

    [Fact]
    public void ParsesLogVersionLine()
    {
        Assert.Equal(new EngineVersion(5, 8, 4),
            ProjectEngine.ParseLogVersion("[2026.11.03-15.20.45:123][  0]LogInit: Engine Version: 5.8.4-58902052+++UE5+Release-5.8"));
        Assert.Null(ProjectEngine.ParseLogVersion("LogInit: Compatible Engine Version: 5.8.0-55116800+++UE5+Release-5.8"));
    }

    [Fact]
    public void ReadsVersionFromMostRecentLog_EvenWhileOpen()
    {
        using var tmp = new TempDir();
        var old = tmp.File(@"Saved\Logs\P-backup.log", "LogInit: Engine Version: 5.7.0-1+++UE5+Release-5.7\n");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-1));
        // As in a real editor log, the version comes after hundreds of startup lines.
        var current = tmp.File(@"Saved\Logs\P.log",
            "Log file open\n" + string.Concat(Enumerable.Repeat("LogWindows: Failed to load 'x.dll'\n", 600)) +
            "LogInit: Engine Version: 5.8.4-58902052+++UE5+Release-5.8\n");
        using var held = new FileStream(current, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);   // like a running editor

        Assert.Equal(new EngineVersion(5, 8, 4), ProjectEngine.ReadVersionFromLog(tmp.Root));
        Assert.Null(ProjectEngine.ReadVersionFromLog(Path.Combine(tmp.Root, "Missing")));
    }

    [Fact]
    public void OnlyTheMostRecentLogIsRead()
    {
        using var tmp = new TempDir();
        var old = tmp.File(@"Saved\Logs\P-backup.log", "LogInit: Engine Version: 5.7.0-1+++UE5+Release-5.7\n");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-1));
        tmp.File(@"Saved\Logs\P.log", "No version in this one\n");
        Assert.Null(ProjectEngine.ReadVersionFromLog(tmp.Root));
    }

    [Fact]
    public void SameRelease_DifferentEngine_NoWarning()
    {
        // Launcher project on a source build of the same release, and the reverse: fine, patches are ignored.
        Assert.Equal(EngineCheck.Ok, ProjectEngine.Check("P", Identify("5.8", [Launcher58, Source58]), Source58, [Launcher58, Source58]));
        Assert.Equal(EngineCheck.Ok, ProjectEngine.Check("P", Identify("{B6}", [Launcher58, Source58]), Launcher58, [Launcher58, Source58]));
    }

    [Fact]
    public void UnregisteredBuild_UsesLogVersion()
    {
        var info = Identify("{OLD}", [Launcher58, Launcher59], new EngineVersion(5, 8, 4));
        Assert.Equal(ProjectVersionSource.ProjectLog, info.Source);
        Assert.Equal(EngineCheck.Ok, ProjectEngine.Check("P", info, Launcher58, [Launcher58, Launcher59]));

        var check = ProjectEngine.Check("P", info, Launcher59, [Launcher58, Launcher59]);
        Assert.Same(Launcher58, check.Suggested);
        Assert.Equal("P uses UE 5.8 (going by its most recent log), but the selected engine is UE 5.9.", check.Warning);
        Assert.Same(Launcher58, ProjectEngine.Preferred(info, [Launcher59, Launcher58]));
    }

    [Fact]
    public void UnregisteredBuild_NoLog_WarnsVersionUnknown_NoSwitch()
    {
        var info = Identify("{OLD}", [Launcher58]);
        Assert.Equal(ProjectVersionSource.UnregisteredBuild, info.Source);
        var check = ProjectEngine.Check("P", info, Launcher58, [Launcher58]);
        Assert.Contains("engine version is unknown", check.Warning);
        Assert.Null(check.Suggested);
        Assert.Null(ProjectEngine.Preferred(info, [Launcher58]));
    }

    [Fact]
    public void DifferentRelease_SwitchOnlyWhenInstalled()
    {
        var installed = ProjectEngine.Check("P", Identify("5.8", [Launcher58, Launcher59]), Launcher59, [Launcher58, Launcher59]);
        Assert.Same(Launcher58, installed.Suggested);

        var missing = ProjectEngine.Check("P", Identify("5.7", [Launcher58]), Launcher58, [Launcher58]);
        Assert.Null(missing.Suggested);
        Assert.Equal("P uses UE 5.7, but the selected engine is UE 5.8. UE 5.7 isn't installed.", missing.Warning);
    }

    [Fact]
    public void PrefersExactAssociation_ThenSameRelease()
    {
        Assert.Same(Source58, ProjectEngine.Preferred(Identify("{B6}", [Launcher58, Source58]), [Launcher58, Source58]));
        // "5.8" with only a 5.8 source build installed: the source build is suggested.
        var info = Identify("5.8", [Source58, Launcher59]);
        Assert.Same(Source58, ProjectEngine.Check("P", info, Launcher59, [Source58, Launcher59]).Suggested);
    }

    [Fact]
    public void NoAssociationOutsideEngineTree_SaysNothing() =>
        Assert.Equal(EngineCheck.Ok, ProjectEngine.Check("P", Identify("", [Launcher58]), Launcher58, [Launcher58]));
}
