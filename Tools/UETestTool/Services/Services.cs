using System.Diagnostics;
using System.IO;
using Drs.Unreal.Common.Launch;

namespace UETestTool.Services;

/// <summary>Dialogs the view model needs, kept behind an interface so the view model has no UI dependencies.</summary>
public interface IUserDialogs
{
    bool Confirm(string title, string message);
    string? PickProjectFile(string? initialDirectory);

    /// <summary>Lets the user edit their search folders. Returns the new list, or null if cancelled.</summary>
    IReadOnlyList<string>? EditSearchFolders(IReadOnlyList<string> folders);
}

public static class AppInfo
{
    /// <summary>
    /// The app's version as set at build time from the git tag (see Directory.Build.props), e.g. <c>0.3.0</c>, or
    /// <c>0.3.1-alpha.0.2</c> for a build made after that tag. The commit hash the SDK appends (<c>+abc123…</c>) is dropped.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        return string.IsNullOrEmpty(info) ? "" : info.Split('+')[0];
    }
}

public static class Launcher
{
    /// <summary>Starts the editor detached. The argument string is passed through untouched (UE parses its own command line).</summary>
    public static void Start(LaunchCommand command)
    {
        var psi = new ProcessStartInfo(command.EditorExe, command.Arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = command.WorkingDirectory,
        };
        using var _ = Process.Start(psi);
    }

    /// <summary>Opens a file with its default app (e.g. the .ini in a text editor).</summary>
    public static void OpenWithShell(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
