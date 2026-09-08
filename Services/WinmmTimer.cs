using System;
using System.Runtime.InteropServices;

namespace ECCR.Services;

/// <summary>
/// Requests Windows' 1ms multimedia timer resolution for the process's lifetime, via the
/// classic <c>timeBeginPeriod</c>/<c>timeEndPeriod</c> pair. Without this, the default system
/// timer resolution (commonly ~15.6ms unless something else already raised it) silently caps
/// how close <see cref="InputDeviceManager"/>'s <c>Task.Delay(4)</c> poll loop and
/// <see cref="ControllerHidService"/>'s <c>Timer(period: 4)</c> can actually get to their coded
/// 4ms/250Hz interval - real-world tick spacing without this call is closer to ~15ms (~66Hz).
/// Call <see cref="Request"/> once near startup and dispose the result once near shutdown
/// (<see cref="MainWindowViewModel"/> does both) - one request/release pair covers both poll
/// loops, since timer resolution is a process-wide (really OS-wide) setting, not per-timer.
/// </summary>
public static class WinmmTimer
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    private sealed class Handle : IDisposable
    {
        private readonly uint _periodMs;
        private bool _released;

        public Handle(uint periodMs) => _periodMs = periodMs;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            try { TimeEndPeriod(_periodMs); } catch { }
        }
    }

    /// <summary>Requests <paramref name="periodMs"/> timer resolution; dispose the result to release it. Never throws - failure just leaves the default (coarser) resolution in effect.</summary>
    public static IDisposable Request(uint periodMs = 1)
    {
        try { TimeBeginPeriod(periodMs); } catch { }
        return new Handle(periodMs);
    }
}
