using Drs.Unreal.Common.Launch;

namespace Drs.Unreal.Common.Tests;

public class LaunchSessionTests
{
    private static ProjectConfigFile FileWith(TempDir tmp, string content) =>
        ProjectConfigFile.Load(tmp.File(@"Config\UETestTool.ini", content));

    [Fact]
    public void NoFile_GivesBuiltInSessions_AllLaunchable()
    {
        using var tmp = new TempDir();
        var file = ProjectConfigFile.Load(Path.Combine(tmp.Root, "missing.ini"));
        var configs = file.GetConfigs();
        var sessions = file.GetSessions(configs);
        Assert.Equal(["Multiplayer"], sessions.Select(s => s.Name));
        Assert.Empty(file.Warnings);

        var plan = sessions[0].Resolve(configs);
        Assert.Equal("Dedicated Server", plan.Server!.Name);
        Assert.Equal(["Client", "Client"], plan.Clients.Select(c => c.Name));
        Assert.Equal("Dedicated Server, Client ×2", sessions[0].Description);
    }

    [Fact]
    public void ProjectSession_MixesConfigs_ServerResolvedWhereverItIsListed()
    {
        using var tmp = new TempDir();
        var file = FileWith(tmp, """
            [Config.Client (noHMD)]
            Template=Client
            Args=-game -log -noHMD

            [Session.Dedicated + noHMD]
            Launch=Client
            Launch=Dedicated Server
            Launch=Client (noHMD)
            """);
        var configs = file.GetConfigs();
        var session = file.GetSessions(configs).Single(s => s.Name == "Dedicated + noHMD");
        Assert.False(session.IsBuiltIn);
        Assert.Empty(file.Warnings);

        var plan = session.Resolve(configs);
        Assert.Null(plan.Error);
        Assert.Equal("Dedicated Server", plan.Server!.Name);
        Assert.Equal(["Client", "Client (noHMD)"], plan.Clients.Select(c => c.Name));
    }

    [Fact]
    public void CustomServerConfig_CountsAsServer()
    {
        using var tmp = new TempDir();
        var file = FileWith(tmp, """
            [Config.Big Server]
            Template=Dedicated Server
            Args=-server -log -port=7778
            [Session.Big]
            Launch=Big Server
            Launch=Client
            """);
        var configs = file.GetConfigs();
        var plan = file.GetSessions(configs).Single(s => s.Name == "Big").Resolve(configs);
        Assert.Equal("Big Server", plan.Server!.Name);
    }

    [Theory]
    [InlineData("Launch=Client\nLaunch=Client", "needs a Dedicated Server or Listen Server")]
    [InlineData("Launch=Dedicated Server\nLaunch=Listen Server", "more than one server")]
    [InlineData("Launch=Dedicated Server\nLaunch=Standalone", "'Standalone' doesn't connect to a server")]
    [InlineData("Launch=Dedicated Server\nLaunch=Gone", "'Gone' isn't one of this project's configurations")]
    [InlineData("", "doesn't list any configurations")]
    public void InvalidSessions_AreKeptWithAWarning(string body, string expected)
    {
        using var tmp = new TempDir();
        var file = FileWith(tmp, "[Session.Bad]\n" + body + "\n");
        var configs = file.GetConfigs();
        var bad = file.GetSessions(configs).Single(s => s.Name == "Bad");
        Assert.Contains(expected, bad.Resolve(configs).Error);
        Assert.Contains(file.Warnings, w => w.Contains("'Bad'") && w.Contains(expected));
    }

    [Fact]
    public void SectionNamedLikeBuiltIn_OverridesIt()
    {
        using var tmp = new TempDir();
        var file = FileWith(tmp, "[Session.Multiplayer]\nLaunch=Listen Server\nLaunch=Client\n");
        var sessions = file.GetSessions(file.GetConfigs());
        Assert.Equal(["Listen Server", "Client"], sessions.Single(s => s.Name == "Multiplayer").Entries);
        Assert.Single(sessions);
        Assert.True(file.DefinesSession("multiplayer"));
    }

    [Fact]
    public void SaveSession_RewritesItsLaunchLines_KeepsCommentsAndOtherSections()
    {
        using var tmp = new TempDir();
        var path = tmp.File(@"Config\UETestTool.ini", """
            ; My session
            [Session.Mine]
            Launch=Dedicated Server
            Launch=Client

            [Config.Other]
            Template=Client
            """);
        var file = ProjectConfigFile.Load(path);
        file.SaveSession("Mine", ["Listen Server", "Client", "Client"]);
        file.SaveSession("New", ["Dedicated Server", "Client"]);

        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Contains("; My session\n[Session.Mine]\nLaunch=Listen Server\nLaunch=Client\nLaunch=Client\n\n[Config.Other]", text);
        Assert.EndsWith("[Session.New]\nLaunch=Dedicated Server\nLaunch=Client\n", text);

        var reloaded = ProjectConfigFile.Load(path);
        Assert.Equal(["Listen Server", "Client", "Client"], reloaded.GetSessions(reloaded.GetConfigs()).Single(s => s.Name == "Mine").Entries);

        Assert.True(reloaded.DeleteSession("Mine"));
        Assert.False(ProjectConfigFile.Load(path).DefinesSession("Mine"));
    }

    [Fact]
    public void SaveSession_InNewFile_WritesHeader()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Root, @"Config\UETestTool.ini");
        ProjectConfigFile.Load(path).SaveSession("S", ["Dedicated Server", "Client"]);
        Assert.StartsWith("; UE Test Tool", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("127.0.0.1", 7778, "127.0.0.1:7778")]
    [InlineData("localhost", 7777, "localhost:7777")]
    [InlineData("192.168.0.10", 7778, "192.168.0.10")]
    [InlineData("127.0.0.1:9000", 7778, "127.0.0.1:9000")]
    public void ConnectingTo_AddsPortToLocalAddressOnly(string connect, int port, string expected)
    {
        var client = LaunchConfig.FindBuiltIn("Client")! with { Rule = new LaunchRule(false, "", connect) };
        Assert.Equal(expected, SessionPlan.ConnectingTo(client, port).Rule.Connect);
    }
}

public class ServerLogTests
{
    private const string ListeningLine =
        "[2026.11.23-14.00.04:330][  0]LogNet: Name:GameNetDriver Def:GameNetDriver IpNetDriver_0 IpNetDriver listening on port 7777";

    [Fact]
    public void ParseListeningPort_FindsGameNetDriverOnly()
    {
        Assert.Equal(7777, ServerLog.ParseListeningPort(ListeningLine));
        Assert.Null(ServerLog.ParseListeningPort("LogTrace: Display: Control listening on port 1985"));
        Assert.Null(ServerLog.ParseListeningPort("LogNet: Name:BeaconNetDriver Def:BeaconNetDriver IpNetDriver_1 IpNetDriver listening on port 15000"));
    }

    [Theory]
    [InlineData("-server -log -abslog=\"D:\\A B\\s.log\" -x", "D:\\A B\\s.log")]
    [InlineData("-server -ABSLOG=D:\\s.log", "D:\\s.log")]
    [InlineData("-server -log=s.log", null)]
    public void FindAbsLog(string args, string? expected) => Assert.Equal(expected, ServerLog.FindAbsLog(args));

    [Fact]
    public void PrepareLogPath_DeletesOldLog_SkipsOneInUse()
    {
        using var tmp = new TempDir();
        var logs = Path.Combine(tmp.Root, "Logs");
        var first = Path.Combine(logs, "P-Server.log");
        Directory.CreateDirectory(logs);
        File.WriteAllText(first, ListeningLine);

        Assert.Equal(first, ServerLog.PrepareLogPath(logs, "P-Server"));
        Assert.False(File.Exists(first));

        File.WriteAllText(first, "running");
        using (new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            Assert.Equal(Path.Combine(logs, "P-Server_2.log"), ServerLog.PrepareLogPath(logs, "P-Server"));
    }

    [Fact]
    public async Task Wait_ReturnsPort_WhenLineIsWrittenLater_WhileFileIsOpenForWriting()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Root, "s.log");
        var wait = ServerLog.WaitForListeningAsync(path, () => false, TimeSpan.FromSeconds(10), CancellationToken.None,
            TimeSpan.FromMilliseconds(20));

        await using (var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)))
        {
            writer.AutoFlush = true;
            await writer.WriteLineAsync("LogInit: starting");
            await Task.Delay(100);
            await writer.WriteAsync(ListeningLine[..40]);   // half a line: not enough yet
            await Task.Delay(100);
            Assert.False(wait.IsCompleted);
            await writer.WriteLineAsync(ListeningLine[40..].Replace("7777", "7790"));
            Assert.Equal((ServerWaitResult.Listening, 7790), await wait);
        }
    }

    [Fact]
    public async Task Wait_FromStartPosition_IgnoresPreviousRunsLine_ThenReadsReplacedFile()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Root, "own.log");
        File.WriteAllText(path, "old run\n" + ListeningLine + "\n");   // last run's server: port 7777

        var wait = ServerLog.WaitForListeningAsync(path, () => false, TimeSpan.FromSeconds(10), CancellationToken.None,
            TimeSpan.FromMilliseconds(20), startPosition: ServerLog.CurrentLength(path));
        await Task.Delay(150);
        Assert.False(wait.IsCompleted);

        File.WriteAllText(path, ListeningLine.Replace("7777", "7801") + "\n");   // the new server replaces the file
        Assert.Equal((ServerWaitResult.Listening, 7801), await wait);
    }

    [Fact]
    public async Task Wait_ReportsExit_AndTimeout()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Root, "never.log");
        Assert.Equal(ServerWaitResult.Exited,
            (await ServerLog.WaitForListeningAsync(path, () => true, TimeSpan.FromSeconds(10), CancellationToken.None)).Result);
        Assert.Equal(ServerWaitResult.TimedOut,
            (await ServerLog.WaitForListeningAsync(path, () => false, TimeSpan.FromMilliseconds(100), CancellationToken.None,
                TimeSpan.FromMilliseconds(20))).Result);
    }

    [Fact]
    public async Task Wait_CanBeCancelled()
    {
        using var tmp = new TempDir();
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ServerLog.WaitForListeningAsync(Path.Combine(tmp.Root, "x.log"), () => false, TimeSpan.FromSeconds(10), cts.Token));
    }
}
