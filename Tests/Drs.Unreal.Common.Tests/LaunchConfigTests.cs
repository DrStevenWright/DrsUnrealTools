using Drs.Unreal.Common.Launch;

namespace Drs.Unreal.Common.Tests;

public class LaunchConfigTests
{
    private static ProjectConfigFile FileWith(TempDir tmp, string content) =>
        ProjectConfigFile.Load(tmp.File(@"Config\UETestTool.ini", content));

    [Fact]
    public void NoFile_GivesBuiltIns()
    {
        using var tmp = new TempDir();
        var file = ProjectConfigFile.Load(Path.Combine(tmp.Root, "missing.ini"));
        Assert.Equal(["Standalone", "Listen Server", "Dedicated Server", "Client"], file.GetConfigs().Select(c => c.Name));
        Assert.Empty(file.Warnings);
    }

    [Fact]
    public void ProjectConfigs_InheritRuleFromTemplate()
    {
        using var tmp = new TempDir();
        var file = FileWith(tmp, """
            [Config.Client (noHMD)]
            Template=Client
            Args=-game -log -noHMD

            [Config.Listen (noHMD)]
            Template=Listen Server
            Args=-game -noHMD
            """);
        var configs = file.GetConfigs();

        var client = configs.Single(c => c.Name == "Client (noHMD)");
        Assert.False(client.Rule.UsesMap);
        Assert.Equal("127.0.0.1", client.Rule.Connect);
        Assert.Equal("Client", client.Base);
        Assert.False(client.IsBuiltIn);

        var listen = configs.Single(c => c.Name == "Listen (noHMD)");
        Assert.Equal("?listen", listen.Rule.MapOptions);
        Assert.Equal(6, configs.Count);
        Assert.Empty(file.Warnings);
    }

    [Fact]
    public void TemplateCanBeEarlierCustomConfig_ArgsInherited_RuleOverridable()
    {
        using var tmp = new TempDir();
        var file = FileWith(tmp, """
            [Config.A]
            Template=Client
            Args=-game -a
            [Config.B]
            Template=A
            Connect=192.168.0.10
            """);
        var b = file.GetConfigs().Single(c => c.Name == "B");
        Assert.Equal("-game -a", b.Args);
        Assert.Equal("192.168.0.10", b.Rule.Connect);
        Assert.Equal("Client", b.Base);
    }

    [Fact]
    public void BuiltInOverride_ReplacesInPlace()
    {
        using var tmp = new TempDir();
        var file = FileWith(tmp, """
            [Config.Standalone]
            Args=-game -log -noHMD
            """);
        var configs = file.GetConfigs();
        Assert.Equal("Standalone", configs[0].Name);
        Assert.Equal("-game -log -noHMD", configs[0].Args);
        Assert.False(configs[0].IsBuiltIn);
        Assert.True(configs[0].Rule.UsesMap);
        Assert.Equal(4, configs.Count);
    }

    [Fact]
    public void UnknownTemplate_WarnsAndFallsBack()
    {
        using var tmp = new TempDir();
        var file = FileWith(tmp, """
            [Config.X]
            Template=Nope
            Map=maybe
            """);
        var x = file.GetConfigs().Single(c => c.Name == "X");
        Assert.True(x.Rule.UsesMap);
        Assert.Equal(2, file.Warnings.Count);
    }

    [Fact]
    public void Save_WritesBaseTemplate_AndRoundTrips_KeepingHandEdits()
    {
        using var tmp = new TempDir();
        var path = tmp.File(@"Config\UETestTool.ini", """
            ; Hand-written note
            [Config.LAN Client]
            Template=Client
            Connect=192.168.0.10
            """);
        var file = ProjectConfigFile.Load(path);
        var lanClient = file.GetConfigs().Single(c => c.Name == "LAN Client");

        file.Save("LAN Client (noHMD)", lanClient, "  -game -noHMD ");

        var reloaded = ProjectConfigFile.Load(path);
        var created = reloaded.GetConfigs().Single(c => c.Name == "LAN Client (noHMD)");
        Assert.Equal("Client", created.Base);
        Assert.Equal("192.168.0.10", created.Rule.Connect);   // template's override copied, not referenced
        Assert.Equal("-game -noHMD", created.Args);
        Assert.Contains("; Hand-written note", File.ReadAllText(path));
        Assert.Contains("Template=Client\r\nArgs=-game -noHMD\r\nConnect=192.168.0.10", File.ReadAllText(path).Replace("\r\n", "\n").Replace("\n", "\r\n"));
    }

    [Fact]
    public void Save_NewFile_GetsExplanatoryHeader_DeleteRemoves()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Root, @"Config\UETestTool.ini");
        var file = ProjectConfigFile.Load(path);
        file.Save("Standalone (noHMD)", LaunchConfig.BuiltIns[0], "-game -noHMD");

        var text = File.ReadAllText(path);
        Assert.StartsWith("; UE Test Tool", text);
        var keyLines = text.Split("\r\n").Where(l => l.Length > 0 && !l.StartsWith(';')).ToList();
        // No rule overrides for a plain built-in template.
        Assert.Equal(["[Config.Standalone (noHMD)]", "Template=Standalone", "Args=-game -noHMD"], keyLines);
        Assert.True(file.Defines("Standalone (noHMD)"));

        Assert.True(file.Delete("Standalone (noHMD)"));
        Assert.DoesNotContain(File.ReadAllLines(path), l => l.StartsWith("[Config."));
        Assert.StartsWith("; UE Test Tool", File.ReadAllText(path));   // explanatory header survives
        Assert.False(file.Delete("Standalone (noHMD)"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" padded", false)]
    [InlineData("a=b", false)]
    [InlineData("[x]", false)]
    [InlineData(";x", false)]
    [InlineData("Client (noHMD, 1920x1080)", true)]
    public void ValidatesNames(string name, bool valid) =>
        Assert.Equal(valid, LaunchConfig.ValidateName(name) is null);

    [Fact]
    public void UserState_RoundTrips_AndDropsEmptyArgs()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Root, @"Saved\UETestTool\User.ini");
        var state = new ProjectUserState { LastMap = "/Game/Maps/Test", LastConfig = "Client (noHMD)" };
        state.CustomArgs["Client (noHMD)"] = "-ResX=1920";
        state.CustomArgs["Standalone"] = "-x";
        state.Save(path);

        state.CustomArgs["Standalone"] = "  ";
        state.Save(path);

        var back = ProjectUserState.Load(path);
        Assert.Equal("/Game/Maps/Test", back.LastMap);
        Assert.Equal("Client (noHMD)", back.LastConfig);
        Assert.Equal("-ResX=1920", back.GetCustomArgs("client (nohmd)"));
        Assert.Equal("", back.GetCustomArgs("Standalone"));
        Assert.DoesNotContain("Standalone=", File.ReadAllText(path));
    }
}
