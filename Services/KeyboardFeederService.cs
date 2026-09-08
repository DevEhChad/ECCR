using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ECCR.Services;

/// <summary>
/// Simulates keyboard key presses for "[Keyboard] ..." targets via the Win32 <c>SendInput</c>
/// API, the fourth virtual-output backend alongside <see cref="VirtualFeederService"/> (Xbox
/// 360), <see cref="DualShock4FeederService"/> and <see cref="VJoyFeederService"/>. Unlike those
/// three, there's no ViGEm/vJoy virtual device involved - this writes directly into the OS
/// keyboard input stream, which every foreground app (games included) reads the same as a real
/// keystroke. <paramref name="targetDeviceId"/> is unused here (no Player/Wheel-Slot concept
/// applies to a keyboard key) - see <see cref="ECCR.Models.MappingEntry.TargetDeviceId"/>'s doc
/// comment for the two namespaces this is a third, N/A case alongside.
/// </summary>
public class KeyboardFeederService : IVirtualFeeder
{
    private readonly object _lock = new();
    private readonly Dictionary<string, bool> _pressedByTarget = new();
    private bool _isActive = true;

    /// <summary>Never raised - a simulated keyboard key can't rumble. Present only to satisfy <see cref="IVirtualFeeder"/>.</summary>
    public event Action<uint, byte, byte>? RumbleReceived { add { } remove { } }

    public void SetActive(bool active)
    {
        lock (_lock)
        {
            _isActive = active;
            if (!active) ReleaseAllLocked();
        }
    }

    public void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue)
    {
        if (!targetOutput.StartsWith("[Keyboard]", StringComparison.OrdinalIgnoreCase)) return;
        // Axes mapped to a keyboard key are treated digitally: pressed once travel crosses the
        // midpoint, released below it - same threshold philosophy as a deadzone-gated button.
        SetKeyState(targetOutput, normalizedValue >= 0.5);
    }

    public void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed)
    {
        if (!targetOutput.StartsWith("[Keyboard]", StringComparison.OrdinalIgnoreCase)) return;
        SetKeyState(targetOutput, isPressed);
    }

    private void SetKeyState(string targetOutput, bool pressed)
    {
        lock (_lock)
        {
            if (!_isActive) return;

            bool wasPressed = _pressedByTarget.TryGetValue(targetOutput, out var prev) && prev;
            if (wasPressed == pressed) return; // dedupe - don't re-send every ~4ms poll tick while held

            if (!KeyboardKeyCatalog.TryGetVirtualKeyCode(ExtractKeyName(targetOutput), out ushort vk))
            {
                _pressedByTarget[targetOutput] = false;
                return;
            }

            SendKeyEvent(vk, pressed);
            _pressedByTarget[targetOutput] = pressed;
        }
    }

    /// <summary>"[Keyboard] Left Ctrl" -> "Left Ctrl".</summary>
    private static string ExtractKeyName(string targetOutput)
    {
        const string prefix = "[Keyboard]";
        string rest = targetOutput.Length > prefix.Length ? targetOutput[prefix.Length..] : string.Empty;
        return rest.Trim();
    }

    public void Reset()
    {
        lock (_lock) { ReleaseAllLocked(); }
    }

    /// <summary>Releases every currently-tracked-down key - called on Reset/deactivate so a stuck key never bleeds into whatever app has focus afterward.</summary>
    private void ReleaseAllLocked()
    {
        foreach (var kvp in _pressedByTarget)
        {
            if (!kvp.Value) continue;
            if (KeyboardKeyCatalog.TryGetVirtualKeyCode(ExtractKeyName(kvp.Key), out ushort vk))
                SendKeyEvent(vk, false);
        }
        _pressedByTarget.Clear();
    }

    /// <summary>
    /// Uses the legacy flat-parameter <c>keybd_event</c> API rather than <c>SendInput</c>'s
    /// struct-based form - deliberately avoids marshaling any P/Invoke struct at all (no
    /// [StructLayout(Explicit)] union to get subtly wrong), since a bad struct layout there
    /// produces an AccessViolationException that no ordinary try/catch can stop, crashing the
    /// whole app the instant a keyboard-mapped input first fires. keybd_event is officially
    /// superseded by SendInput but remains fully functional on modern Windows; the try/catch
    /// below is defense in depth so any future interop failure here degrades to "the key press
    /// silently doesn't happen" instead of taking the app down, matching how every other feeder
    /// backend isolates its own hardware-call failures.
    /// </summary>
    private static void SendKeyEvent(ushort vk, bool keyDown)
    {
        try
        {
            keybd_event((byte)vk, 0, keyDown ? 0u : KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch { }
    }

    public void Dispose()
    {
        try { Reset(); } catch { }
    }

    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
