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

    [ObservableProperty]
    private double _deadzone = 0.0;

    [ObservableProperty]
    private bool _isInverted = false;

    [ObservableProperty]
    private int _rawMin = 0;

    [ObservableProperty]
    private int _rawMax = 65535;

    [ObservableProperty]
    private int _rawCenter = 32767;

    /// <summary>
    /// Set when this entry was created/captured via the Visual Binder's graphical hotspot
    /// (see <see cref="ECCR.ViewModels.VisualBinderViewModel"/>), naming which
    /// <see cref="ECCR.Models.SemanticButton"/> it belongs to so re-clicking that hotspot finds
    /// this same entry again instead of creating a duplicate. Null for entries created via the
    /// ordinary Button Mapping list or Auto-Bind - those code paths never set it.
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

    [JsonIgnore]
    public bool IsAxis => SourceType == InputType.Axis;

    partial void OnSourceTypeChanged(InputType value)
    {
        OnPropertyChanged(nameof(IsAxis));
    }

    /// <summary>
    /// False for a fresh "Click to Bind" placeholder row (or one still mid-capture) - used to
    /// hide the rename pencil on unbound rows, since every unbound row shares the same
    /// placeholder <see cref="SourceDeviceName"/>/<see cref="SourceType"/>/<see cref="SourceIndex"/>
    /// (see <c>MainWindowViewModel.UncategorizedGamepadDeviceName</c>/<c>UncategorizedWheelDeviceName</c>),
    /// so renaming one before it's bound would silently rename every other unbound row too.
    /// </summary>
    [JsonIgnore]
    public bool IsBound => SourceDisplayName != "Click to Bind" && !SourceDisplayName.StartsWith("Listening...", StringComparison.Ordinal);

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

    partial void OnTargetOutputChanged(string value)
    {
        OnPropertyChanged(nameof(IsWheelTarget));
        OnPropertyChanged(nameof(IsKeyboardTarget));
        OnPropertyChanged(nameof(ShowPlayerCombo));
        OnPropertyChanged(nameof(ShowWheelSlotCombo));
    }

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
            double offset = normalized - 0.5;
            double halfDz = Deadzone * 0.5;

            if (Math.Abs(offset) < halfDz)
            {
                offset = 0.0;
            }
            else
            {
                // Rescale the remaining travel outside the deadzone back up to the full
                // 0..0.5 half-range so output still reaches 0.0/1.0 at the physical extremes.
                offset = Math.Sign(offset) * ((Math.Abs(offset) - halfDz) / (0.5 - halfDz)) * 0.5;
            }

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