using System.Text;
using System.Text.RegularExpressions;

namespace Drs.Unreal.Common.Launch;

public enum ServerWaitResult { Listening, Exited, TimedOut }

/// <summary>
/// Finds out when a launched server is ready for clients, by watching its log for the game net driver's
/// <c>LogNet: Name:GameNetDriver ... IpNetDriver listening on port 7777</c> line.
/// </summary>
public static partial class ServerLog
{
    [GeneratedRegex(@"GameNetDriver\b.*IpNetDriver listening on port (\d+)")]
    private static partial Regex ListeningRegex();

    [GeneratedRegex(@"(?:^|\s)-abslog=(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex AbsLogRegex();

    /// <summary>The port from a "GameNetDriver … listening on port N" log line, else null.</summary>
    public static int? ParseListeningPort(string line) =>
        ListeningRegex().Match(line) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var port) ? port : null;

    /// <summary>The path given with <c>-abslog=</c> in a command line, if any.</summary>
    public static string? FindAbsLog(string arguments) =>
        AbsLogRegex().Match(arguments) is { Success: true } m
            ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            : null;

    /// <summary>The argument that sends the log to <paramref name="path"/>.</summary>
    public static string AbsLogArgument(string path) => $"-abslog=\"{path}\"";

    /// <summary>
    /// A log path in <paramref name="logsDir"/> for a new server, named <c>&lt;baseName&gt;.log</c> (or <c>_2</c>, <c>_3</c>…
    /// if that one is in use by a server that's still running). Any old file at the chosen path is deleted, so whatever
    /// appears there is from the new server.
    /// </summary>
    public static string PrepareLogPath(string logsDir, string baseName)
    {
        Directory.CreateDirectory(logsDir);
        for (var i = 1; ; i++)
        {
            var path = Path.Combine(logsDir, i == 1 ? $"{baseName}.log" : $"{baseName}_{i}.log");
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return path;
            }
            catch (IOException) when (i < 20) { }          // open in a running server: try the next name
            catch (UnauthorizedAccessException) when (i < 20) { }
        }
    }

    /// <summary>The size of the file at <paramref name="path"/>, or 0 if there isn't one.</summary>
    public static long CurrentLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (IOException) { return 0; }
    }

    /// <summary>
    /// Waits until the log at <paramref name="logPath"/> says the server is listening (returning its port), the server
    /// exits, or <paramref name="timeout"/> passes. The log doesn't have to exist yet. Throws if cancelled.
    /// </summary>
    /// <param name="startPosition">
    /// Where to start reading: the length of a previous run's log at this path (see <see cref="CurrentLength"/>), so
    /// its "listening" line isn't mistaken for the new server's. If the file is replaced by a shorter one, reading
    /// restarts from its beginning.
    /// </param>
    public static async Task<(ServerWaitResult Result, int Port)> WaitForListeningAsync(
        string logPath, Func<bool> hasExited, TimeSpan timeout, CancellationToken cancel,
        TimeSpan? pollInterval = null, long startPosition = 0)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        var deadline = DateTime.UtcNow + timeout;
        var position = startPosition;
        var partial = "";
        var decoder = new UTF8Encoding(false).GetDecoder();
        var buffer = new byte[64 * 1024];
        var chars = new char[new UTF8Encoding(false).GetMaxCharCount(buffer.Length)];

        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            // Check before reading: a server that exits right after listening has still listened.
            var exited = hasExited();

            try
            {
                if (File.Exists(logPath))
                {
                    using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length < position) { position = 0; partial = ""; }   // the file was recreated
                    stream.Position = position;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, cancel)) > 0)
                    {
                        position += read;
                        var count = decoder.GetChars(buffer, 0, read, chars, 0);
                        var text = partial + new string(chars, 0, count);
                        var lines = text.Split('\n');
                        partial = lines[^1];
                        foreach (var line in lines.AsSpan(0, lines.Length - 1))
                            if (ParseListeningPort(line) is { } port) return (ServerWaitResult.Listening, port);
                    }
                }
            }
            catch (IOException) { }   // momentarily unavailable: try again next time

            if (exited) return (ServerWaitResult.Exited, 0);
            if (DateTime.UtcNow >= deadline) return (ServerWaitResult.TimedOut, 0);
            await Task.Delay(interval, cancel);
        }
    }
}
