using System.Diagnostics;
using System.IO;
using Drs.Unreal.Common.Launch;

namespace UETestTool.Services;

/// <summary>Dialogs the view model needs, kept behind an interface so the view model has no UI dependencies.</summary>
public interface IUserDialogs
{
    bool Confirm(string title, string message);
    string? PickProjectFile(string? initialDirectory);
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
