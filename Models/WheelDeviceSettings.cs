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
    /// Shows every physical input the device reports (not just already-bound ones) as
    /// placeholder rows in the Rebinding Table - see <c>BinderPageViewModel.RebuildDisplayRows</c>.
    /// Off by default so selecting a device doesn't immediately dump its full capability list
    /// into the table; the user opts in via the table's "Show All Inputs" toggle.
    /// </summary>
    [ObservableProperty]
    private bool _showAllPhysicalInputs = false;

    private static string InputKey(InputType type, int index) => $"{type}:{index}";

    /// <summary>
    /// Physical inputs (keyed as "Button:{index}" / "Axis:{index}") the user has explicitly
    /// dismissed from the Rebinding Table's "Show All Inputs" placeholder listing (see
    /// <c>BinderPageViewModel.RebuildDisplayRows</c>) - e.g. buttons on a 40-button wheel base
    /// they'll never bind and don't want cluttering the table. Real, already-bound entries are
    /// never affected by this - only unbound placeholder rows are ever hidden.
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
}
