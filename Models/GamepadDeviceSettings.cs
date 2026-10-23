using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

/// <summary>
/// How a PlayStation touchpad's single physical click switch is disambiguated using the active
/// finger's touch position at the moment of the click - see
/// <c>MainWindowViewModel.ProcessInputPolling</c>'s multi-zone touchpad handling. A click with no
/// valid touch packet (gloved hand, edge contact) always falls back to <see cref="Unified"/>-style
/// full-panel behavior regardless of the selected mode.
/// </summary>
public enum TouchpadClickMode
{
    /// <summary>Every physical click is "Touchpad Click (Full)" - finger position is ignored entirely.</summary>
    Unified,
    /// <summary>Click resolves to left or right half by X position only (no top band).</summary>
    SplitLeftRight,
    /// <summary>Click resolves to top band (by Y), else left/right half (by X) - see <see cref="GamepadDeviceSettings.TouchpadTopSplitFraction"/>.</summary>
    MultiZone
}

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

    /// <summary>0-100: scales the heavy/low-frequency rumble motor (the strong, slow motor in the left grip on a standard Xbox-layout pad) before it's written to the physical controller. Replaces the old single combined RumbleIntensityPercent - a profile saved before this field existed just gets the 100 default here, same as any newly-added field.</summary>
    [ObservableProperty]
    private int _heavyMotorIntensityPercent = 100;

    /// <summary>0-100: scales the light/high-frequency rumble motor (the weaker, buzzier motor in the right grip).</summary>
    [ObservableProperty]
    private int _lightMotorIntensityPercent = 100;

    /// <summary>
    /// Whether this device's own visual mapping diagram is collapsed on the Device Mappings tab,
    /// so the Device Mapping Table can use the full width - per-device rather than a global app
    /// setting, so switching gamepads restores each one's own saved layout choice.
    /// <see cref="JsonPropertyNameAttribute"/> keeps the serialized key stable as "IsVisualBinderHidden"
    /// (its name before this property was renamed for terminology consistency) so profile JSON
    /// files saved before this rename still deserialize without a migration step.
    /// </summary>
    [JsonPropertyName("IsVisualBinderHidden")]
    [ObservableProperty]
    private bool _isVisualDeviceMappingHidden = false;

    /// <summary>PlayStation touchpad only - see <see cref="Models.TouchpadClickMode"/>. Unified by default so existing behavior (every click = full-panel) is unchanged until a user opts in.</summary>
    [ObservableProperty]
    private TouchpadClickMode _touchpadClickMode = TouchpadClickMode.Unified;

    /// <summary>0.20-0.40: fraction of the touchpad's height (from the top) treated as the "top band" in <see cref="Models.TouchpadClickMode.MultiZone"/> - tunable like the existing Deadzone sliders. Unused in the other two modes.</summary>
    [ObservableProperty]
    private double _touchpadTopSplitFraction = 0.30;

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

    /// <summary>DualSense adaptive-trigger effects - see <see cref="TriggerEffectSettings"/>. Edits to the nested object are re-raised as a change of this property (<see cref="OnLeftTriggerChanged"/>) so the existing per-device autosave hook in <c>MainWindowViewModel</c> sees them.</summary>
    [ObservableProperty]
    private TriggerEffectSettings _leftTrigger = new();

    [ObservableProperty]
    private TriggerEffectSettings _rightTrigger = new();

    public GamepadDeviceSettings()
    {
        LeftTrigger.PropertyChanged += OnLeftTriggerEdited;
        RightTrigger.PropertyChanged += OnRightTriggerEdited;
    }

    // Profile deserialization replaces the instances created above, so each swap moves the forwarding subscription over.
    partial void OnLeftTriggerChanged(TriggerEffectSettings? oldValue, TriggerEffectSettings newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= OnLeftTriggerEdited;
        if (newValue != null) newValue.PropertyChanged += OnLeftTriggerEdited; // null only from a hand-edited profile
    }

    partial void OnRightTriggerChanged(TriggerEffectSettings? oldValue, TriggerEffectSettings newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= OnRightTriggerEdited;
        if (newValue != null) newValue.PropertyChanged += OnRightTriggerEdited;
    }

    private void OnLeftTriggerEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(LeftTrigger));

    private void OnRightTriggerEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(RightTrigger));
}
