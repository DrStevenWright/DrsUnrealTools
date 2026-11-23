using System.Text.Json;

namespace Drs.Unreal.Common.Projects;

/// <summary>The bits of a .uproject file the tools care about.</summary>
public sealed record UProjectFile(string Path, string? EngineAssociation)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path)!;

    public static UProjectFile Read(string uprojectPath)
    {
        string? association = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(uprojectPath), Json.LenientOptions);
            if (doc.RootElement.TryGetProperty("EngineAssociation", out var a) && a.ValueKind == JsonValueKind.String)
                association = a.GetString();
        }
        catch (Exception) { }
        return new UProjectFile(uprojectPath, association);
    }
}
