using System;

namespace ECCR.Services;

public class VirtualFeederService : IDisposable
{
    private readonly ViGEmFeederService _vigem = new();
    private readonly VJoyFeederService _vjoy = new();

    public VirtualFeederService()
    {
        _vigem.Initialize();
        _vjoy.Initialize();
    }

    public void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed)
    {
        if (string.IsNullOrWhiteSpace(targetOutput)) return;

        if (targetOutput.Contains("[Xbox]", StringComparison.OrdinalIgnoreCase) ||
            targetOutput.StartsWith("Xbox", StringComparison.OrdinalIgnoreCase) ||
            targetOutput.Contains("D-Pad", StringComparison.OrdinalIgnoreCase))
        {
            _vigem.DispatchButton(targetOutput, isPressed);
        }
        else
        {
            _vjoy.UpdateButton(targetDeviceId, targetOutput, isPressed);
        }
    }

    public void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(targetOutput)) return;

        if (targetOutput.Contains("[Xbox]", StringComparison.OrdinalIgnoreCase) ||
            targetOutput.StartsWith("Xbox", StringComparison.OrdinalIgnoreCase) ||
            targetOutput.Contains("Trigger", StringComparison.OrdinalIgnoreCase) ||
            targetOutput.Contains("Stick", StringComparison.OrdinalIgnoreCase))
        {
            _vigem.DispatchAxis(targetOutput, normalizedValue);
        }
        else
        {
            _vjoy.UpdateAxis(targetDeviceId, targetOutput, normalizedValue);
        }
    }

    public void Dispose()
    {
        _vigem.Shutdown();
        _vjoy.Shutdown();
    }
}