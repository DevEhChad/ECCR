using System;

namespace ECCR.Services;

/// <summary>
/// Fans mapping output out to the two virtual backends the app supports: ViGEm
/// (<c>[Xbox] ...</c> targets) and vJoy (<c>[Wheel] ...</c> targets), so a single
/// Player target can combine axes/buttons from multiple physical rigs into
/// whichever virtual device type each individual mapping asks for.
/// </summary>
public class CompositeFeederService : IVirtualFeeder
{
    private readonly VirtualFeederService _xboxFeeder = new();
    private readonly VJoyFeederService _wheelFeeder = new();

    public bool IsActive => _xboxFeeder.IsActive;

    public void SetActive(bool active)
    {
        _xboxFeeder.SetActive(active);
        _wheelFeeder.SetActive(active);
    }

    public void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue)
    {
        if (targetOutput.StartsWith("[Wheel]", StringComparison.OrdinalIgnoreCase))
            _wheelFeeder.UpdateAxis(targetDeviceId, targetOutput, normalizedValue);
        else
            _xboxFeeder.UpdateAxis(targetDeviceId, targetOutput, normalizedValue);
    }

    public void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed)
    {
        if (targetOutput.StartsWith("[Wheel]", StringComparison.OrdinalIgnoreCase))
            _wheelFeeder.UpdateButton(targetDeviceId, targetOutput, isPressed);
        else
            _xboxFeeder.UpdateButton(targetDeviceId, targetOutput, isPressed);
    }

    public void Reset()
    {
        _xboxFeeder.Reset();
        _wheelFeeder.Reset();
    }

    public void Dispose()
    {
        _xboxFeeder.Dispose();
        _wheelFeeder.Dispose();
    }
}
