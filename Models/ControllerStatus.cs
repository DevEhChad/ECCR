using CommunityToolkit.Mvvm.ComponentModel;
using ECCR.Services;

namespace ECCR.Models;

/// <summary>How a controller is currently connected, as reported by the HID report layout observed (see <see cref="ECCR.Services.ControllerHidService"/>).</summary>
public enum ControllerConnectionType
{
    Unknown,
    Usb,
    Bluetooth,
    /// <summary>Wireless, but XInput can't say whether over Bluetooth or an Xbox Wireless Adapter.</summary>
    Wireless
}

/// <summary>
/// Battery/connection info for one gamepad-class device, sourced from raw HID reports rather
/// than DirectInput (which has no concept of battery level). One instance lives for as long as
/// the device stays connected (see <c>DeviceStatusBarViewModel</c>/<c>ConnectedDevicesPageViewModel</c>),
/// with fields mutated in place on each ~2s poll tick from
/// <see cref="ECCR.Services.ControllerHidService"/> so bound UI updates live rather than only
/// on reconnect. Populated for Sony devices (DualSense/DualShock 4) only; every other device
/// keeps <see cref="IsBatterySupported"/>/<see cref="IsLedSupported"/> false and the UI falls
/// back to name-only display.
/// </summary>
public partial class ControllerStatus : ObservableObject
{
    public string DeviceInstanceName { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public int ProductId { get; set; }

    [ObservableProperty]
    private DeviceHardwareCategory _category = DeviceHardwareCategory.GenericGamepad;

    [ObservableProperty]
    private bool _isBatterySupported;

    [ObservableProperty]
    private int? _batteryPercent;

    [ObservableProperty]
    private bool _isCharging;

    [ObservableProperty]
    private ControllerConnectionType _connectionType = ControllerConnectionType.Unknown;

    [ObservableProperty]
    private bool _isLedSupported;

    /// <summary>True for Sony devices this service actively parses gyro/touch data from (same population scope as battery).</summary>
    [ObservableProperty]
    private bool _isMotionSupported;

    /// <summary>Gyro rate, normalized to roughly -1..1 for "aim sensitivity" use rather than physical units - see <see cref="ECCR.Services.ControllerHidService"/>'s parsing comment.</summary>
    [ObservableProperty]
    private double _gyroPitch;

    [ObservableProperty]
    private double _gyroYaw;

    [ObservableProperty]
    private double _gyroRoll;

    [ObservableProperty]
    private bool _touchActive;

    /// <summary>Touch X/Y normalized 0..1 across the touchpad surface; meaningless while <see cref="TouchActive"/> is false.</summary>
    [ObservableProperty]
    private double _touchX;

    [ObservableProperty]
    private double _touchY;
}
