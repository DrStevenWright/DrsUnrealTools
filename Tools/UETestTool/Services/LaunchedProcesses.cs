using System.Diagnostics;

namespace UETestTool.Services;

/// <summary>
/// The editor instances this app has launched that are still running, so Stop can close them. Not persisted: after a
/// restart of the app, instances it launched earlier aren't tracked.
/// </summary>
public sealed class LaunchedProcesses
{
    private readonly List<(Process Process, string Label)> _running = [];
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    /// <summary>Raised (on the UI thread) when an instance is added or exits.</summary>
    public event Action? Changed;

    public int Count
    {
        get { lock (_running) return _running.Count; }
    }

    public IReadOnlyList<string> Labels
    {
        get { lock (_running) return _running.Select(r => r.Label).ToList(); }
    }

    public void Add(Process process, string label)
    {
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Remove(process);
        lock (_running) _running.Add((process, label));
        if (HasExitedSafe(process)) Remove(process);   // exited before Exited was hooked up
        else RaiseChanged();
    }

    /// <summary>
    /// Asks every instance to close (as clicking its window's close button would), then ends any that are still running
    /// after <paramref name="grace"/>. A dedicated server's console window can't be asked, so it's ended straight away.
    /// Returns how many were stopped.
    /// </summary>
    public async Task<int> StopAllAsync(TimeSpan grace)
    {
        List<Process> processes;
        lock (_running) processes = _running.Select(r => r.Process).ToList();
        if (processes.Count == 0) return 0;

        var toKill = new List<Process>();
        foreach (var p in processes)
        {
            try
            {
                p.Refresh();
                if (p.HasExited) continue;
                if (p.MainWindowHandle == IntPtr.Zero || !p.CloseMainWindow()) toKill.Add(p);
            }
            catch (InvalidOperationException) { }   // already gone
        }

        var deadline = DateTime.UtcNow + grace;
        foreach (var p in processes.Except(toKill))
        {
            var left = deadline - DateTime.UtcNow;
            if (left > TimeSpan.Zero)
            {
                using var timeout = new CancellationTokenSource(left);
                try { await p.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
            }
            if (!HasExitedSafe(p)) toKill.Add(p);
        }

        foreach (var p in toKill)
        {
            try { p.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }   // exiting already, or access denied
        }
        return processes.Count;
    }

    private void Remove(Process process)
    {
        bool removed;
        lock (_running) removed = _running.RemoveAll(r => ReferenceEquals(r.Process, process)) > 0;
        if (!removed) return;
        process.Dispose();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        if (_context is null || SynchronizationContext.Current == _context) Changed?.Invoke();
        else _context.Post(_ => Changed?.Invoke(), null);
    }

    private static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; }
        catch (InvalidOperationException) { return true; }
    }
}
