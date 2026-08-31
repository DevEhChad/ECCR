using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

/// <summary>
/// Per-device wheel tuning, saved inside <see cref="UserProfile.WheelSettings"/> keyed by
/// <c>MappingEntry.SourceDeviceName</c>. Distinct from a mapping row's own
/// <see cref="MappingEntry.Deadzone"/>/<see cref="MappingEntry.RawMin"/>/
/// <see cref="MappingEntry.RawMax"/> (which calibrate one axis's raw range), since rotation and
/// force feedback apply to the whole wheel rather than any single axis. An <see cref="ObservableObject"/>
/// (rather than a plain POCO) so the Wheel Settings tab can bind directly to a live instance and
/// so <c>MainWindowViewModel</c> can subscribe to <see cref="ObservableObject.PropertyChanged"/>
/// the same way it already does for <see cref="MappingEntry"/> - see
/// <c>MainWindowViewModel.OnWheelSettingsPropertyChanged</c> - to auto-save and to re-sync
/// <see cref="ECCR.Services.WheelForceFeedbackService"/> whenever a value here changes.
/// </summary>
public partial class WheelDeviceSettings : ObservableObject
{
    /// <summary>
    /// Degrees of physical rotation this wheel is set to (matches whatever the wheel's own
    /// firmware/driver is configured for - ECCR can't change a wheel's actual rotation range,
    /// this is purely informational plus used to scale how "off-center" a Spring effect's
    /// torque calculation treats a given raw steering position).
    /// </summary>
    [ObservableProperty]
    private int _rotationDegrees = 900;

    [ObservableProperty]
    private bool _forceFeedbackEnabled = true;

    /// <summary>0-200: scales every force magnitude sent to the wheel before it's written (100 = pass the game's requested force through unchanged).</summary>
    [ObservableProperty]
    private int _forceFeedbackStrengthPercent = 100;

    /// <summary>Center-seeking spring effects (DirectInput "Condition" effects using a symmetric center point).</summary>
    [ObservableProperty]
    private bool _springEffectEnabled = true;

    /// <summary>Velocity-resisting damper effects.</summary>
    [ObservableProperty]
    private bool _damperEffectEnabled = true;

    /// <summary>Static/kinetic friction effects.</summary>
    [ObservableProperty]
    private bool _frictionEffectEnabled = true;

    /// <summary>Flips force direction - some wheel/game combinations need this to turn the "correct" way.</summary>
    [ObservableProperty]
    private bool _invertForce = false;
}
