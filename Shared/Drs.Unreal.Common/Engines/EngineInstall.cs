namespace Drs.Unreal.Common.Engines;

public enum EngineSource
{
    /// <summary>Installed by the Epic Games Launcher.</summary>
    Launcher,

    /// <summary>A source build registered in the user's registry (identifier is a GUID).</summary>
    Source,
}

/// <summary>An Unreal Engine installation with a usable editor executable.</summary>
/// <param name="Identifier">
/// The value a .uproject's <c>EngineAssociation</c> uses for this engine:
/// e.g. <c>5.8</c> for a launcher install, <c>{GUID}</c> for a registered source build.
/// </param>
/// <param name="RootDir">The folder that contains <c>Engine\</c>.</param>
/// <param name="Version">Version from <c>Engine\Build\Build.version</c>, if readable.</param>
/// <param name="EditorExe">Full path of UnrealEditor.exe (or UE4Editor.exe for UE4).</param>
public sealed record EngineInstall(
    string Identifier,
    string RootDir,
    EngineSource Source,
    EngineVersion? Version,
    string EditorExe)
{
    public string DisplayName => Source switch
    {
        EngineSource.Launcher => $"{Version?.ToString() ?? Identifier} (Launcher)",
        _ => $"{Version?.ToString() ?? "Unknown version"} (Source: {RootDir})",
    };

    public override string ToString() => DisplayName;
}

public sealed record EngineVersion(int Major, int Minor, int Patch)
{
    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    /// <summary>"5.8": the part that matters for compatibility.</summary>
    public string Release => $"{Major}.{Minor}";

    /// <summary>Same major.minor; patch releases are compatible with each other.</summary>
    public bool SameRelease(EngineVersion other) => Major == other.Major && Minor == other.Minor;
}
