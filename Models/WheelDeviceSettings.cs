using System.Collections.Generic;
using System.Text.Json.Serialization;
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

    /// <summary>Independent inertia (resistance-to-acceleration) toggle - previously folded into <see cref="DamperEffectEnabled"/>.</summary>
    [ObservableProperty]
    private bool _inertiaEffectEnabled = true;

    /// <summary>0-200: per-effect-type gain layered on top of <see cref="ForceFeedbackStrengthPercent"/> (100 = no extra scaling).</summary>
    [ObservableProperty]
    private int _springGainPercent = 100;

    [ObservableProperty]
    private int _damperGainPercent = 100;

    [ObservableProperty]
    private int _inertiaGainPercent = 100;

    [ObservableProperty]
    private int _frictionGainPercent = 100;

    /// <summary>0-100: floor a nonzero commanded force magnitude is boosted up to, so weak forces (e.g. light road texture) aren't lost to the wheel's own static friction - 0 (default) disables boosting.</summary>
    [ObservableProperty]
    private int _minimumForcePercent = 0;

    /// <summary>0-100: soft ceiling every effect magnitude is clamped to before it reaches the wheel, as a fraction of DirectInput's own -10000..10000 range - 100 (default) applies no extra cap beyond that native range.</summary>
    [ObservableProperty]
    private int _maxTorquePercent = 100;

    /// <summary>0 (off, default) to 5: how aggressively <see cref="ECCR.Services.WheelForceFeedbackService"/> interpolates a constant/periodic effect's magnitude toward each newly-decoded value instead of snapping to it, smoothing coarse ~60Hz game telemetry steps into a more continuous feel. 0 reproduces today's exact instant-snap behavior with no added overhead.</summary>
    [ObservableProperty]
    private int _reconstructionFilterLevel = 0;

    /// <summary>Runs a synthetic, always-on centering spring independent of anything the game sends - for titles with no native self-aligning torque, and as the baseline "doesn't go limp" feel under <see cref="SynthesizeFfbFromRumbleEnabled"/>.</summary>
    [ObservableProperty]
    private bool _standaloneCenterSpringEnabled = false;

    /// <summary>0-100: stiffness of the standalone center spring above.</summary>
    [ObservableProperty]
    private int _standaloneCenterSpringStrengthPercent = 50;

    /// <summary>Injects a synthetic high-resistance spring wall (a "bump stop") at <see cref="SoftLockRotationDegrees"/>, narrower than the wheel's actual configured <see cref="RotationDegrees"/> - lets a wide-rotation direct-drive base emulate a narrower wheel's lock-to-lock feel.</summary>
    [ObservableProperty]
    private bool _softLockEnabled = false;

    /// <summary>Degrees of rotation the soft-lock wall engages at - must be less than <see cref="RotationDegrees"/> to have any effect; defaults to matching it (i.e. no-op) until the user narrows it.</summary>
    [ObservableProperty]
    private int _softLockRotationDegrees = 900;

    /// <summary>0-100: how hard the soft-lock wall resists rotation past <see cref="SoftLockRotationDegrees"/>.</summary>
    [ObservableProperty]
    private int _softLockStrengthPercent = 100;

    /// <summary>
    /// Opt-in for Mode B: when this wheel's steering axis is routed to an "[Xbox]"/"[DS4]" target
    /// instead of the vJoy "[Wheel] Steering" target (so the game only ever sends simple
    /// XInput/DS4 rumble, never real DirectInput FFB effects), synthesizes wheel forces from that
    /// rumble signal instead of leaving the wheel with no feedback at all. Off by default so a
    /// user who deliberately wants a limp wheel in gamepad-output mode isn't surprised by it, and
    /// so it never fights <see cref="ForceFeedbackEnabled"/>'s Mode A passthrough.
    /// </summary>
    [ObservableProperty]
    private bool _synthesizeFfbFromRumbleEnabled = false;

    /// <summary>
    /// Live clipping-meter telemetry for the Wheel Settings tab, written by
    /// <see cref="ECCR.Services.WheelForceFeedbackService"/> on every effect rebuild - not
    /// persisted. Approximates "would the configured Strength/gain have clamped one of this
    /// effect's own parameters," not real hardware output torque (SharpDX/DirectInput exposes
    /// no such telemetry). <see cref="ECCR.ViewModels.MainWindowViewModel.OnWheelSettingsPropertyChanged"/>
    /// excludes these two properties from triggering an FFB resync/auto-save, since they change
    /// on every incoming FFB packet.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private double _liveOutputForcePercent = 0.0;

    [JsonIgnore]
    [ObservableProperty]
    private bool _isClipping = false;

    /// <summary>
    /// Legacy/migration-source only: superseded by <see cref="ECCR.Models.HardwareDeviceProfile.ShowAllPhysicalInputs"/>
    /// in the global <see cref="ECCR.Services.HardwareProfileService"/> store, which
    /// <c>DeviceMappingPageViewModel</c>/<c>InputMatrixViewModel</c> read/write now instead of this
    /// field, since a per-profile "Show All Inputs" state per device made no sense once nicknames
    /// moved out of per-profile storage. Kept only so old profile JSON still deserializes and
    /// <see cref="ECCR.Services.HardwareProfileService.MigrateFromLegacyIfNeeded"/> can seed the
    /// global store from it once per profile on load; nothing else reads/writes this anymore.
    /// </summary>
    [ObservableProperty]
    private bool _showAllPhysicalInputs = false;

    private static string InputKey(InputType type, int index) => $"{type}:{index}";

    /// <summary>
    /// Legacy/migration-source only - see <see cref="ShowAllPhysicalInputs"/>'s doc comment.
    /// Superseded by <see cref="ECCR.Models.HardwareDeviceProfile.HiddenPlaceholderInputs"/>.
    /// </summary>
    public HashSet<string> HiddenPlaceholderInputs { get; set; } = new();

    public bool IsInputHidden(InputType type, int index) => HiddenPlaceholderInputs.Contains(InputKey(type, index));

    /// <summary>Dismisses (or restores) one placeholder row, then raises <see cref="HiddenPlaceholderInputs"/>'s PropertyChanged so the existing auto-save hook fires.</summary>
    public void SetInputHidden(InputType type, int index, bool hidden)
    {
        string key = InputKey(type, index);
        if (hidden) HiddenPlaceholderInputs.Add(key);
        else HiddenPlaceholderInputs.Remove(key);
        OnPropertyChanged(nameof(HiddenPlaceholderInputs));
    }

    /// <summary>Restores every placeholder row the user has dismissed for this device.</summary>
    public void ClearHiddenPlaceholderInputs()
    {
        if (HiddenPlaceholderInputs.Count == 0) return;
        HiddenPlaceholderInputs.Clear();
        OnPropertyChanged(nameof(HiddenPlaceholderInputs));
    }

    /// <summary>
    /// Legacy/migration-source only - see <see cref="ShowAllPhysicalInputs"/>'s doc comment.
    /// Superseded by <see cref="ECCR.Models.HardwareDeviceProfile.ButtonNicknames"/> in the
    /// global, profile-independent <see cref="ECCR.Services.HardwareProfileService"/> store -
    /// per-profile nicknames meant switching a game profile silently gave a device a different
    /// nickname set, which defeated the point of naming a physical button.
    /// </summary>
    public Dictionary<string, string> ButtonNicknames { get; set; } = new();

    /// <summary>Null if this input has no nickname set (or the entry is blank/whitespace-only).</summary>
    public string? GetButtonNickname(InputType type, int index) =>
        ButtonNicknames.TryGetValue(InputKey(type, index), out var nick) && !string.IsNullOrWhiteSpace(nick) ? nick : null;

    /// <summary>Sets (or, given null/whitespace, clears) one input's nickname, then raises <see cref="ButtonNicknames"/>'s PropertyChanged so the existing auto-save hook fires.</summary>
    public void SetButtonNickname(InputType type, int index, string? nickname)
    {
        string key = InputKey(type, index);
        if (string.IsNullOrWhiteSpace(nickname)) ButtonNicknames.Remove(key);
        else ButtonNicknames[key] = nickname.Trim();
        OnPropertyChanged(nameof(ButtonNicknames));
    }
}
