using System.Collections.Generic;
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

    /// <summary>
    /// User-editable display-name overrides for this device's physical buttons/axes, keyed as
    /// "Button:{index}" / "Axis:{index}" (a plain string key rather than a compound key type,
    /// for simple System.Text.Json round-tripping through a Dictionary). Missing/empty means
    /// "use DevicePresetService's generated name" - see <see cref="ECCR.Services.DevicePresetService.GetEffectiveDisplayName"/>.
    /// Exists mainly for wheel-class hardware whose physical button legend ECCR can't infer
    /// (most brands besides Moza don't distinguish a PlayStation-legend vs. Xbox-legend SKU in
    /// <see cref="ECCR.Services.DeviceHardwareCategory"/>), but works for any device.
    /// </summary>
    /// <summary>
    /// User-editable override for the whole device's display name (e.g. "My Wheel" instead of
    /// the raw DirectInput instance name "MOZA Racing MOZA R9"). Null/empty means show the raw
    /// name. Keyed the same way as this whole settings object (by <c>MappingEntry.SourceDeviceName</c>),
    /// so two identical physical devices sharing the same DirectInput instance name also share
    /// this nickname - a known limitation, same as <see cref="InputNicknames"/> below.
    /// </summary>
    [ObservableProperty]
    private string? _deviceNickname = null;

    public Dictionary<string, string> InputNicknames { get; set; } = new();

    private static string NicknameKey(InputType type, int index) => $"{type}:{index}";

    public string? GetNickname(InputType type, int index) =>
        InputNicknames.TryGetValue(NicknameKey(type, index), out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>Sets or clears (via null/whitespace) one physical input's nickname, then raises <see cref="InputNicknames"/>'s PropertyChanged so the existing auto-save hook (<c>MainWindowViewModel.OnWheelSettingsPropertyChanged</c>) fires.</summary>
    public void SetNickname(InputType type, int index, string? nickname)
    {
        string key = NicknameKey(type, index);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            InputNicknames.Remove(key);
        }
        else
        {
            InputNicknames[key] = nickname.Trim();
        }
        OnPropertyChanged(nameof(InputNicknames));
    }
}
