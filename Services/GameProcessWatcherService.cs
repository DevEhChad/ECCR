using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ECCR.Services;

/// <summary>
/// Polls which process owns the foreground window, on its own independent timer - the same
/// "poll periodically in the background" pattern already used by <see cref="InputDeviceManager"/>'s
/// hotplug watcher and <see cref="ControllerHidService"/>'s device refresh, rather than a
/// <c>SetWinEventHook</c> callback. Only raises <see cref="ForegroundExecutableChanged"/> when the
/// resolved executable path actually changes since the last tick, so a caller doing per-tick work
/// (a library lookup, a profile switch) isn't repeating it every poll while the same app stays
/// focused.
/// </summary>
public class GameProcessWatcherService : IDisposable
{
    private const int PollIntervalMs = 1000;

    private Timer? _timer;
    private string? _lastExecutablePath;

    /// <summary>Fired on a background thread (the timer's own callback) whenever the foreground process's resolved exe path changes - null when it can't be resolved (desktop/explorer focused, or an inaccessible/elevated process). Subscribers touching UI-bound state must marshal back to the UI thread themselves.</summary>
    public event Action<string?>? ForegroundExecutableChanged;

    public void Start()
    {
        _timer = new Timer(_ => Poll(), null, dueTime: PollIntervalMs, period: PollIntervalMs);
    }

    private void Poll()
    {
        string? exePath = ResolveForegroundExecutablePath();
        if (string.Equals(exePath, _lastExecutablePath, StringComparison.OrdinalIgnoreCase)) return;

        _lastExecutablePath = exePath;
        ForegroundExecutableChanged?.Invoke(exePath);
    }

    private static string? ResolveForegroundExecutablePath()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            // Elevated/protected process (access denied), or it exited between the two calls -
            // treat as "can't resolve," not an error.
            return null;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
