using System;

namespace ECCR.Services;

/// <summary>
/// One poll's worth of raw hardware state for a single physical device, as read by
/// <see cref="InputDeviceManager"/> and broadcast to <see cref="MainWindowViewModel"/> via
/// <c>OnInputPolled</c>. <see cref="Axes"/> is always 8 elements in the fixed order
/// X, Y, Z, RotationX, RotationY, RotationZ, Slider0, Slider1 (raw DirectInput 0-65535
/// values, uncalibrated); <see cref="Buttons"/> is 32 elements normally, expanded to 132 to
/// fold a POV hat's four directions into indices 128-131 when the device reports one.
/// </summary>
public class RawDeviceInputState
{
    public Guid InstanceGuid { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int[] Axes { get; set; } = [];
    public bool[] Buttons { get; set; } = [];

    /// <summary>Raw POV hat angle in hundredths of a degree (0 = up, clockwise), or -1 when centered/no POV present - the value <see cref="InputDeviceManager"/> decodes into <see cref="Buttons"/>' 128-131 synthetic indices before discarding; kept here too for diagnostics (the Input Matrix's raw-packet inspector).</summary>
    public int PovAngleHundredths { get; set; } = -1;
}