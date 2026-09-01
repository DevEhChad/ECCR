using System;

namespace ECCR.Models;

/// <summary>
/// Per-tick snapshot of one physical device's raw input, for live UI animation only (the
/// Visual Binder's moving stick graphics and pressed-button highlights) - deliberately
/// independent of any <see cref="MappingEntry"/> calibration/deadzone, unlike the values fed
/// to the virtual output feeders in <c>MainWindowViewModel.ProcessInputPolling</c>.
/// </summary>
public class LiveDeviceInputState
{
    public string DeviceName { get; init; } = string.Empty;
    public Guid InstanceGuid { get; init; }
    public bool[] Buttons { get; init; } = new bool[132];
    public double[] NormalizedAxes { get; init; } = new double[8];
    public bool HasSticks { get; init; }
    public double LeftStickX { get; init; } = 0.5;
    public double LeftStickY { get; init; } = 0.5;
    public double RightStickX { get; init; } = 0.5;
    public double RightStickY { get; init; } = 0.5;
}
