using System;

namespace ECCR.Services;

/// <summary>
/// Fans mapping output out to the four virtual backends the app supports: ViGEm Xbox 360
/// (<c>[Xbox] ...</c> targets), ViGEm DualShock4 (<c>[DS4] ...</c> targets), vJoy
/// (<c>[Wheel] ...</c> targets) and simulated keyboard keys (<c>[Keyboard] ...</c> targets), so
/// a single Player target can combine axes/buttons from multiple physical rigs into whichever
/// virtual device type each individual mapping asks for. Also aggregates the ViGEm backends'
/// rumble feedback into one event so a single subscriber (<c>MainWindowViewModel</c>) can
/// forward it to whichever physical device is mapped to that player slot, regardless of which
/// virtual pad type triggered it.
/// </summary>
public class CompositeFeederService : IVirtualFeeder
{
    private readonly VirtualFeederService _xboxFeeder = new();
    private readonly DualShock4FeederService _ds4Feeder = new();
    private readonly VJoyFeederService _wheelFeeder = new();
    private readonly KeyboardFeederService _keyboardFeeder = new();

    /// <summary>Raised whenever a game rumbles either virtual pad type: (targetDeviceId, largeMotor, smallMotor).</summary>
    public event Action<uint, byte, byte>? RumbleReceived;

    public bool IsActive => _xboxFeeder.IsActive;

    public CompositeFeederService()
    {
        _xboxFeeder.RumbleReceived += (id, large, small) => RumbleReceived?.Invoke(id, large, small);
        _ds4Feeder.RumbleReceived += (id, large, small) => RumbleReceived?.Invoke(id, large, small);
    }

    public void SetActive(bool active)
    {
        _xboxFeeder.SetActive(active);
        _ds4Feeder.SetActive(active);
        _wheelFeeder.SetActive(active);
        _keyboardFeeder.SetActive(active);
    }

    public void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue)
    {
        if (targetOutput.StartsWith("[Wheel]", StringComparison.OrdinalIgnoreCase))
            _wheelFeeder.UpdateAxis(targetDeviceId, targetOutput, normalizedValue);
        else if (targetOutput.StartsWith("[DS4]", StringComparison.OrdinalIgnoreCase))
            _ds4Feeder.UpdateAxis(targetDeviceId, targetOutput, normalizedValue);
        else if (targetOutput.StartsWith("[Keyboard]", StringComparison.OrdinalIgnoreCase))
            _keyboardFeeder.UpdateAxis(targetDeviceId, targetOutput, normalizedValue);
        else
            _xboxFeeder.UpdateAxis(targetDeviceId, targetOutput, normalizedValue);
    }

    public void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed)
    {
        if (targetOutput.StartsWith("[Wheel]", StringComparison.OrdinalIgnoreCase))
            _wheelFeeder.UpdateButton(targetDeviceId, targetOutput, isPressed);
        else if (targetOutput.StartsWith("[DS4]", StringComparison.OrdinalIgnoreCase))
            _ds4Feeder.UpdateButton(targetDeviceId, targetOutput, isPressed);
        else if (targetOutput.StartsWith("[Keyboard]", StringComparison.OrdinalIgnoreCase))
            _keyboardFeeder.UpdateButton(targetDeviceId, targetOutput, isPressed);
        else
            _xboxFeeder.UpdateButton(targetDeviceId, targetOutput, isPressed);
    }

    public void Reset()
    {
        _xboxFeeder.Reset();
        _ds4Feeder.Reset();
        _wheelFeeder.Reset();
        _keyboardFeeder.Reset();
    }

    public void Dispose()
    {
        // Isolated so one backend throwing during teardown can't skip releasing the others.
        try { _xboxFeeder.Dispose(); } catch { }
        try { _ds4Feeder.Dispose(); } catch { }
        try { _wheelFeeder.Dispose(); } catch { }
        try { _keyboardFeeder.Dispose(); } catch { }
    }
}
