using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

/// <summary>
/// Per-device gamepad tuning (rumble + LED), saved inside <see cref="UserProfile.GamepadSettings"/>
/// keyed by <c>MappingEntry.SourceDeviceName</c> - the gamepad-class counterpart of
/// <see cref="WheelDeviceSettings"/>. An <see cref="ObservableObject"/> for the same reason that
/// class is: so device-specific tabs can bind directly to a live instance and
/// <c>MainWindowViewModel</c> can subscribe to <see cref="ObservableObject.PropertyChanged"/> to
/// auto-save whenever a value here changes (see <c>MainWindowViewModel.OnGamepadSettingsPropertyChanged</c>).
/// </summary>
public partial class GamepadDeviceSettings : ObservableObject
{
    [ObservableProperty]
    private bool _rumbleEnabled = true;

    /// <summary>0-100: scales every rumble motor value a game sends before it's written to the physical controller.</summary>
    [ObservableProperty]
    private int _rumbleIntensityPercent = 100;

    [ObservableProperty]
    private byte _ledRed = 0;

    [ObservableProperty]
    private byte _ledGreen = 122;

    [ObservableProperty]
    private byte _ledBlue = 255;

    [ObservableProperty]
    private LedEffectMode _ledMode = LedEffectMode.Static;

    [ObservableProperty]
    private double _ledSpeed = 1.0;
}
