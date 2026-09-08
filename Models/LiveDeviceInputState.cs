using System;

namespace ECCR.Models;

/// <summary>
/// Per-tick snapshot of one physical device's raw input, for live UI animation only (the
/// Visual Device Mapping's moving stick graphics and pressed-button highlights) - deliberately
/// independent of any <see cref="MappingEntry"/> calibration/deadzone, unlike the values fed
/// to the virtual output feeders in <c>MainWindowViewModel.ProcessInputPolling</c>.
/// </summary>
public class LiveDeviceInputState
{
    public string DeviceName { get; init; } = string.Empty;
    public Guid InstanceGuid { get; init; }
    public bool[] Buttons { get; init; } = new bool[132];
    public double[] NormalizedAxes { get; init; } = new double[8];

    /// <summary>Raw, uncalibrated 0-65535 axis values (same fixed order as <see cref="NormalizedAxes"/>) - for the Input Matrix's live gauges/raw-packet inspector, which want the actual hardware value alongside the normalized percentage.</summary>
    public int[] RawAxes { get; init; } = new int[8];

    /// <summary>Raw POV hat angle in hundredths of a degree, or -1 when centered/no POV - see <see cref="RawDeviceInputState.PovAngleHundredths"/>.</summary>
    public int PovAngleHundredths { get; init; } = -1;
    public bool HasSticks { get; init; }
    public double LeftStickX { get; init; } = 0.5;
    public double LeftStickY { get; init; } = 0.5;
    public double RightStickX { get; init; } = 0.5;
    public double RightStickY { get; init; } = 0.5;
}
