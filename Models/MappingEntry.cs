using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

/// <summary>
/// One binding: a single physical axis or button on a physical device, routed to a single
/// virtual output channel. This is the core unit of the whole app - a profile is just a
/// list of these, the main window's grid is one row per entry, and
/// <c>MainWindowViewModel.ProcessInputPolling</c> walks the full list on every DirectInput
/// poll to find matches and feed the virtual devices.
/// <see cref="TargetOutput"/> is a plain display string (e.g. "[Xbox] Xbox A (Cross / South
/// / Handbrake)" or "[Wheel] Steering (Axis X)") rather than an enum: its "[Xbox]"/"[Wheel]"
/// prefix is what <see cref="ECCR.Services.CompositeFeederService"/> pattern-matches on to
/// decide whether an entry feeds the ViGEm or vJoy backend, and the rest of the string is
/// pattern-matched again downstream to pick the specific axis/button. This keeps the UI's
/// dropdown list, the auto-bind presets, and the feeders all working off one shared
/// vocabulary of strings instead of a brittle parallel enum that would need to stay in sync
/// with three places at once - the tradeoff is that renaming a target string anywhere means
/// grepping for every place that matches against it.
/// </summary>
public partial class MappingEntry : ObservableObject
{
    [JsonIgnore]
    [ObservableProperty]
    private bool _isSelected = false;

    /// <summary>True while this is the entry currently armed for capture (<see cref="ECCR.ViewModels.MainWindowViewModel.ListeningEntry"/>) - distinct from <see cref="IsSelected"/>, which means "checked for a bulk Select All/Remove Selected action." Drives the Rebinding Table row's highlight and the Visual Binder hotspot's matching ring, whichever side armed it.</summary>
    [JsonIgnore]
    [ObservableProperty]
    private bool _isArmed = false;

    /// <summary>True while this is a button-type mapping whose physical input is currently held down - live feedback only, not persisted, populated from <see cref="ECCR.ViewModels.MainWindowViewModel.OnLiveDeviceStateUpdated"/> the same way the Visual Binder's hotspot press state is.</summary>
    [JsonIgnore]
    [ObservableProperty]
    private bool _isLivePressed = false;

    [ObservableProperty]
    private string _sourceDeviceName = string.Empty;

    [ObservableProperty]
    private Guid _sourceDeviceGuid = Guid.Empty;

    [ObservableProperty]
    private InputType _sourceType = InputType.Button;

    [ObservableProperty]
    private int _sourceIndex = 0;

    [ObservableProperty]
    private string _sourceDisplayName = "Click to Bind";

    /// <summary>
    /// Numeric virtual-device slot this entry's output routes to - a ViGEm Xbox360/DS4
    /// controller number ("Player", see <see cref="ECCR.ViewModels.MainWindowViewModel.PlayerTargets"/>)
    /// for an "[Xbox]"/"[DS4]" <see cref="TargetOutput"/>, or a vJoy device rID ("Wheel Slot",
    /// see <see cref="ECCR.ViewModels.MainWindowViewModel.WheelSlots"/>) for a "[Wheel]" one.
    /// One field serves both purposes rather than two, since <see cref="TargetOutput"/>'s prefix
    /// already unambiguously says which namespace a given value lives in at every consumer
    /// (<see cref="ECCR.Services.CompositeFeederService"/> and friends) - a second field would
    /// just be a second place for the two to drift out of sync, the same duplication bug
    /// <see cref="WheelRotationDegrees"/> above already illustrates.
    /// </summary>
    [ObservableProperty]
    private uint _targetDeviceId = 1;

    [ObservableProperty]
    private string _targetOutput = "[Xbox] Xbox A (Cross / South / Handbrake)";

    /// <summary>Inner deadzone: normalized travel near the axis's rest point (center for a stick/steering axis, zero for a pedal) that's clamped flat. See <see cref="CalculateCalibratedValue"/>.</summary>
    [ObservableProperty]
    private double _deadzone = 0.0;

    /// <summary>Outer deadzone: normalized travel near the axis's physical extreme that's clamped to that extreme (1.0, or 0.0 for a centered axis's low end) - lets a wheel/pedal that reaches slightly past its "real" useful range still report a clean 0/1 there. 0 = no outer clamp (the default, matching every profile saved before this field existed).</summary>
    [ObservableProperty]
    private double _deadzoneOuter = 0.0;

    /// <summary>Response curve exponent applied after deadzones: 1.0 (default) is linear passthrough, &gt;1.0 gives more precision near center/rest at the cost of the extremes, &lt;1.0 the reverse. See <see cref="CalculateCalibratedValue"/>.</summary>
    [ObservableProperty]
    private double _linearityExponent = 1.0;

    [ObservableProperty]
    private bool _isInverted = false;

    /// <summary>Pedal/trigger axes only (ignored for centered axes - sticks/steering): snaps output directly to 1.0 the instant travel passes the inner <see cref="Deadzone"/>, skipping the normal <see cref="LinearityExponent"/> ramp entirely - digital-microswitch-style "hair trigger" behavior, e.g. for LT/RT.</summary>
    [ObservableProperty]
    private bool _isHairTriggerMode = false;

    [ObservableProperty]
    private int _rawMin = 0;

    [ObservableProperty]
    private int _rawMax = 65535;

    [ObservableProperty]
    private int _rawCenter = 32767;

    /// <summary>
    /// Set when this entry was created/captured via the Binder page's graphical diagram
    /// (see <see cref="ECCR.ViewModels.BinderDiagramViewModel"/>), naming which
    /// <see cref="ECCR.Models.SemanticButton"/>/<see cref="ECCR.Models.WheelSemanticInput"/> it
    /// belongs to so re-clicking that hotspot finds this same entry again instead of creating a
    /// duplicate. Null for entries created via the Rebinding Table's "+ Add Input" or Auto-Bind
    /// - those code paths never set it.
    /// </summary>
    [ObservableProperty]
    private string? _semanticHotspot = null;

    /// <summary>
    /// Superseded by the device-level <see cref="ECCR.Models.WheelDeviceSettings.RotationDegrees"/>
    /// (rotation is a whole-wheel property, not a per-axis-mapping one - having both was the
    /// bug: the Calibration dialog's dial and the Wheel Settings tab could disagree). Kept only
    /// so old profile JSON still deserializes and <c>MainWindowViewModel.MigrateLegacyWheelRotationDegrees</c>
    /// can seed the new device-level value from it once on load; nothing else reads this anymore.
    /// </summary>
    [Obsolete("Superseded by WheelDeviceSettings.RotationDegrees; kept only for one-time profile migration.")]
    [ObservableProperty]
    private int _wheelRotationDegrees = 900;

    [JsonIgnore]
    [ObservableProperty]
    private double _liveRawPercentage = 0.0;

    [JsonIgnore]
    [ObservableProperty]
    private double _liveOutputPercentage = 0.0;

    [JsonIgnore]
    [ObservableProperty]
    private int _latestRawReading = 0;

    /// <summary>
    /// True only for a row synthesized fresh by <c>BinderPageViewModel.RebuildDisplayRows</c> to
    /// represent a physical input the selected device reports that has no <see cref="MappingEntry"/>
    /// yet - never added to <c>MainWindowViewModel.Mappings</c> and never persisted, unlike the
    /// generic "Click to Bind" placeholders (see <see cref="IsBound"/>'s doc comment), since a
    /// placeholder row already knows its real <see cref="SourceType"/>/<see cref="SourceIndex"/>/
    /// <see cref="SourceDisplayName"/> - it only lacks a <see cref="TargetOutput"/>. Promoted into
    /// a real, registered entry the moment the user interacts with it (see
    /// <c>MainWindowViewModel.EnsureMappingRegistered</c>).
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private bool _isPlaceholder = false;

    /// <summary><see cref="TargetOutput"/>, or a friendlier placeholder string while <see cref="IsPlaceholder"/> is true and nothing's been assigned yet.</summary>
    [JsonIgnore]
    public string TargetOutputDisplay => IsPlaceholder && string.IsNullOrEmpty(TargetOutput) ? "Not yet assigned" : TargetOutput;

    [JsonIgnore]
    public bool IsAxis => SourceType == InputType.Axis;

    partial void OnSourceTypeChanged(InputType value)
    {
        OnPropertyChanged(nameof(IsAxis));
    }

    /// <summary>
    /// False for a fresh "Click to Bind" placeholder row (or one still mid-capture) - drives the
    /// "Clear Binding" flyout item's visibility in <c>RebindingTableView</c>, since there's
    /// nothing to unbind on a row that isn't bound yet.
    /// </summary>
    [JsonIgnore]
    public bool IsBound => !IsPlaceholder && SourceDisplayName != "Click to Bind" && !SourceDisplayName.StartsWith("Listening...", StringComparison.Ordinal);

    partial void OnIsPlaceholderChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBound));
        OnPropertyChanged(nameof(TargetOutputDisplay));
    }

    /// <summary>
    /// True when <see cref="TargetOutput"/> is a "[Keyboard] ..." target (routed to
    /// <see cref="ECCR.Services.KeyboardFeederService"/>). Like <see cref="IsWheelTarget"/>,
    /// drives row UI - a keyboard target has no Player/Wheel Slot concept, so both of those
    /// dropdowns hide for it.
    /// </summary>
    [JsonIgnore]
    public bool IsKeyboardTarget => TargetOutput.StartsWith("[Keyboard]", StringComparison.OrdinalIgnoreCase);

    /// <summary>Row UI convenience: whether the Player-slot combo should show for this row's target (Xbox/DS4, not Wheel or Keyboard).</summary>
    [JsonIgnore]
    public bool ShowPlayerCombo => !IsWheelTarget && !IsKeyboardTarget;

    /// <summary>Row UI convenience: whether the Wheel-Slot combo should show for this row's target.</summary>
    [JsonIgnore]
    public bool ShowWheelSlotCombo => IsWheelTarget && !IsKeyboardTarget;

    partial void OnSourceDisplayNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsBound));
    }

    /// <summary>
    /// True when <see cref="TargetOutput"/> is a "[Wheel] ..." target (routed to the vJoy
    /// backend, see <see cref="ECCR.Services.CompositeFeederService"/>), false for "[Xbox]"/
    /// "[DS4]" targets (ViGEm). Drives which dropdown a mapping row shows for
    /// <see cref="TargetDeviceId"/> - Wheel Slot options for wheel targets, Player options for
    /// everything else - since the two share the same underlying field but are different
    /// numeric namespaces (vJoy device rID vs. ViGEm/DS4 controller number).
    /// </summary>
    [JsonIgnore]
    public bool IsWheelTarget => TargetOutput.StartsWith("[Wheel]", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this is the wheel's steering axis specifically - drives the Calibration tab's embedded rotation gauge and "Center Offset" display, which only make sense for steering (not pedals).</summary>
    [JsonIgnore]
    public bool IsSteeringAxis => TargetOutput.Contains("Steer", StringComparison.OrdinalIgnoreCase);

    /// <summary>How far <see cref="RawCenter"/> sits from the theoretical RawMin/RawMax midpoint - 0 for a wheel/stick that's never had "Set Center" used (or one that happens to rest exactly centered). Purely a display value; the actual calibration math in <see cref="CalculateCalibratedValue"/> reads <see cref="RawCenter"/> directly.</summary>
    [JsonIgnore]
    public int CenterOffset => RawCenter - (RawMin + RawMax) / 2;

    partial void OnTargetOutputChanged(string value)
    {
        OnPropertyChanged(nameof(IsWheelTarget));
        OnPropertyChanged(nameof(IsKeyboardTarget));
        OnPropertyChanged(nameof(ShowPlayerCombo));
        OnPropertyChanged(nameof(ShowWheelSlotCombo));
        OnPropertyChanged(nameof(TargetOutputDisplay));
        OnPropertyChanged(nameof(IsSteeringAxis));
    }

    partial void OnRawCenterChanged(int value) => OnPropertyChanged(nameof(CenterOffset));
    partial void OnRawMinChanged(int value) => OnPropertyChanged(nameof(CenterOffset));
    partial void OnRawMaxChanged(int value) => OnPropertyChanged(nameof(CenterOffset));

    /// <summary>
    /// Turns one raw DirectInput axis reading (0-65535) into a calibrated 0.0-1.0 output
    /// value, applying (in order) the user's min/max/center calibration range, deadzone, and
    /// inversion. Called once per poll per axis mapping from
    /// <c>MainWindowViewModel.ProcessInputPolling</c>, so it also doubles as the live-reading
    /// update for the Calibration dialog's bars/dial (<see cref="LatestRawReading"/>,
    /// <see cref="LiveRawPercentage"/>, <see cref="LiveOutputPercentage"/>).
    /// </summary>
    public double CalculateCalibratedValue(int rawVal)
    {
        LatestRawReading = rawVal;
        LiveRawPercentage = Math.Clamp(rawVal / 65535.0, 0.0, 1.0);

        // Rescale the raw value into the user-calibrated [RawMin, RawMax] window so a wheel
        // or pedal that never quite reaches the DirectInput extremes still maps to a full
        // 0..1 output range.
        int min = Math.Min(RawMin, RawMax);
        int max = Math.Max(RawMin, RawMax);
        if (max - min <= 0) max = min + 1;

        double clamped = Math.Clamp(rawVal, min, max);
        double normalized = (clamped - min) / (double)(max - min);

        // Sticks/steering rest at the middle of their travel (0.5), not at zero, so their
        // deadzone has to be carved out symmetrically around the center rather than from the
        // low end - otherwise a small deadzone would only mask drift on one side of center.
        bool isCenteredAxis = TargetOutput.Contains("Stick") || TargetOutput.Contains("Steer") || TargetOutput.Contains("Steering");

        if (isCenteredAxis)
        {
            // RawCenter marks the physical resting position ("Set Center") - normalized into the
            // same [0,1] window as `normalized` so travel below/above center rescales against
            // its own actual available range, rather than assuming center sits exactly at the
            // RawMin/RawMax midpoint. A wheel/stick that's genuinely off-center (RawCenter not
            // equidistant from RawMin/RawMax) still reaches a clean 0.0/1.0 at both physical
            // extremes this way instead of clipping early on the shorter side. When RawCenter is
            // still at its default (the midpoint), centerNormalized is exactly 0.5 and every step
            // below is numerically identical to the pre-existing fixed-0.5 math.
            double centerNormalized = (double)(Math.Clamp(RawCenter, min, max) - min) / (max - min);

            double rawSideOffset = normalized - centerNormalized;
            double sideRange = rawSideOffset < 0 ? centerNormalized : 1.0 - centerNormalized;
            if (sideRange <= 0.0) sideRange = 0.0001; // center pinned at (or past) an extreme - guard against div-by-zero

            // From here, `mag` is "how far into this side's own travel, 0..1" - same shape as the
            // pedal branch's own deadzone/outer/linearity steps below, just applied per-side
            // instead of to the whole 0..1 pedal range.
            double mag = Math.Abs(rawSideOffset) / sideRange;

            if (mag < Deadzone)
            {
                mag = 0.0;
            }
            else
            {
                mag = (mag - Deadzone) / (1.0 - Deadzone);
            }

            if (DeadzoneOuter > 0.0 && mag > 1.0 - DeadzoneOuter)
            {
                mag = 1.0;
            }

            if (LinearityExponent != 1.0 && mag > 0.0)
            {
                mag = Math.Pow(mag, LinearityExponent);
            }

            mag = Math.Clamp(mag, 0.0, 1.0);
            double offset = Math.Sign(rawSideOffset) * mag * 0.5;
            normalized = 0.5 + offset;
        }
        else
        {
            // Pedals/triggers rest at 0, so their deadzone is a simple low-end cutoff.
            if (Deadzone > 0.0)
            {
                if (normalized < Deadzone)
                    normalized = 0.0;
                else
                    normalized = (normalized - Deadzone) / (1.0 - Deadzone);
            }

            // Hair-trigger mode: snap straight to full output the instant travel clears the
            // inner deadzone, bypassing the outer-deadzone snap (already a no-op once normalized
            // is 1.0) and the linearity curve below entirely - digital-microswitch behavior
            // rather than a ramp.
            if (IsHairTriggerMode && normalized > 0.0)
            {
                normalized = 1.0;
            }

            // Outer deadzone: travel within DeadzoneOuter of full trigger/pedal travel snaps to 1.0.
            if (DeadzoneOuter > 0.0 && normalized > 1.0 - DeadzoneOuter)
            {
                normalized = 1.0;
            }

            if (LinearityExponent != 1.0 && normalized > 0.0)
            {
                normalized = Math.Pow(normalized, LinearityExponent);
            }
        }

        if (IsInverted)
        {
            normalized = 1.0 - normalized;
        }

        LiveOutputPercentage = Math.Clamp(normalized, 0.0, 1.0);
        return LiveOutputPercentage;
    }
    
    // Not currently read anywhere - badge glyph/color for a mapping's physical input is
    // instead computed on the fly by Converters/ButtonBadgeConverter.cs (its own BadgeInfo
    // type). Left here in case that's not obvious from a grep; safe to remove if unused.
    public string Glyph { get; set; } = string.Empty;
    public string Foreground { get; set; } = string.Empty;
}