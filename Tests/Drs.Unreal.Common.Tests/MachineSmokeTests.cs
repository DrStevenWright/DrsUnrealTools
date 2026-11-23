using System.Diagnostics;
using Drs.Unreal.Common.Engines;
using Drs.Unreal.Common.Maps;
using Drs.Unreal.Common.Projects;
using Xunit.Abstractions;

namespace Drs.Unreal.Common.Tests;

/// <summary>
/// Runs discovery against the real machine and prints what it finds. Asserts nothing machine-specific,
/// so it passes anywhere; read the output (dotnet test --logger "console;verbosity=detailed").
/// </summary>
public class MachineSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public void DiscoverEverything()
    {
        var sw = Stopwatch.StartNew();
        var engines = EngineDiscovery.Discover();
        output.WriteLine($"Engines ({sw.ElapsedMilliseconds} ms):");
        foreach (var e in engines) output.WriteLine($"  [{e.Identifier}] {e.DisplayName} -> {e.EditorExe}");

        sw.Restart();
        var projects = ProjectDiscovery.Discover([], engines.Select(e => e.RootDir));
        output.WriteLine($"Projects ({sw.ElapsedMilliseconds} ms):");
        foreach (var p in projects)
        {
            var file = UProjectFile.Read(p.UProjectPath);
            var engine = EngineDiscovery.ResolveAssociation(file.EngineAssociation, p.UProjectPath, engines);
            output.WriteLine($"  {file.Name} [{p.Source}] assoc={file.EngineAssociation} -> {engine?.DisplayName ?? "NO MATCH"}  {p.UProjectPath}");
        }

        foreach (var p in projects)
        {
            sw.Restart();
            var maps = MapDiscovery.Discover(Path.GetDirectoryName(p.UProjectPath)!);
            output.WriteLine($"Maps in {Path.GetFileName(p.UProjectPath)} ({maps.Count}, {sw.ElapsedMilliseconds} ms):");
            foreach (var m in maps.Take(8)) output.WriteLine($"  {m.PackagePath}");
            if (maps.Count > 8) output.WriteLine($"  … and {maps.Count - 8} more");
        }
    }
}
