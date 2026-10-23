using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>Coarse hardware family, inferred from a device's DirectInput name string by <see cref="DevicePresetService.DetectCategory"/>.</summary>
public enum DeviceHardwareCategory
{
    MozaWheel,
    LogitechRig,
    FanatecRig,
    ThrustmasterRig,
    SimagicRig,
    GenericWheelOrPedals,
    PlayStationController,
    NintendoController,
    XboxGamepad,
    GenericGamepad
}

/// <summary>
/// Which virtual output backend a preset/Output-As switch targets - the same three backends
/// <see cref="CompositeFeederService"/> fans mapping output out to. Kept as its own small enum
/// (rather than reusing <see cref="ECCR.Models.VirtualEmulationMode"/>, which only distinguishes
/// two) so <see cref="DevicePresetService.GenerateAutoBindPreset"/> and
/// <see cref="ECCR.ViewModels.AutoMapWizardViewModel"/> can express all three real backends.
/// </summary>
public enum OutputCategory
{
    Xbox,
    DualShock4,
    Wheel
}

/// <summary>
/// What Auto Map binds a device to. <see cref="Xbox360"/> and <see cref="DualShock4"/> put everything on the
/// ViGEm pad; <see cref="DirectInput"/> uses the generic vJoy wheel layout; <see cref="Combined"/> binds
/// recognised Xbox buttons to the Xbox pad and the wheel's axes (plus any other button) to the vJoy wheel.
/// </summary>
public enum AutoBindMode
{
    Xbox360,
    DirectInput,
    Combined,
    DualShock4
}

/// <summary>Result of <see cref="DevicePresetService.GenerateAutoBindPreset"/>: the rows, why the layout was chosen, and the mode Auto Map would pre-select.</summary>
public sealed record AutoBindResult(List<PresetMappingItem> Items, string LayoutDescription, AutoBindMode SuggestedMode);

/// <summary>Canonical Xbox button names used by <c>xboxButtons</c> in <c>wheel_profiles.json</c>, and their "[Xbox] ..." target strings.</summary>
public static class XboxButtonNames
{
    public const string A = "A", B = "B", X = "X", Y = "Y", LB = "LB", RB = "RB";
    public const string View = "View", Menu = "Menu", Guide = "Guide", LSB = "LSB", RSB = "RSB";
    public const string DpadUp = "DpadUp", DpadDown = "DpadDown", DpadLeft = "DpadLeft", DpadRight = "DpadRight";

    private static readonly Dictionary<string, string> Targets = new(StringComparer.OrdinalIgnoreCase)
    {
        [A] = "[Xbox] Xbox A (Cross / South / Handbrake)",
        [B] = "[Xbox] Xbox B (Circle / East)",
        [X] = "[Xbox] Xbox X (Square / West / Shift Down)",
        [Y] = "[Xbox] Xbox Y (Triangle / North / Shift Up)",
        [LB] = "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)",
        [RB] = "[Xbox] Xbox RB (Right Bumper / R1)",
        [LSB] = "[Xbox] Xbox LSB (Left Stick Click / L3)",
        [RSB] = "[Xbox] Xbox RSB (Right Stick Click / R3)",
        [Menu] = "[Xbox] Xbox Menu (Start / Options)",
        [View] = "[Xbox] Xbox View (Back / Map / Share)",
        [Guide] = "[Xbox] Xbox Guide (Home / Guide)",
        [DpadUp] = "[Xbox] D-Pad Up",
        [DpadDown] = "[Xbox] D-Pad Down",
        [DpadLeft] = "[Xbox] D-Pad Left",
        [DpadRight] = "[Xbox] D-Pad Right",
    };

    public static bool TryGetTarget(string name, out string target) => Targets.TryGetValue(name, out target!);
}

/// <summary>
/// Everything about mapping a *specific* piece of hardware, keyed primarily by its DirectInput
/// instance name string (matched by substring in <see cref="DetectCategory"/>), with an optional
/// USB VID/PID available at most call sites for the more reliable <see cref="DetectCategoryByVidPid"/>
/// path and the <see cref="WheelProfileService"/> (<c>wheel_profiles.json</c>) override lookup -
/// see <see cref="ResolveCategoryAndProfile"/>, the single path both go through. Three things
/// live here that all depend on that same category detection: friendly display names for raw
/// button/axis indices (<see cref="GetButtonDisplayName"/>/<see cref="GetAxisDisplayName"/>, used
/// by the "Click to Map" listener and the Calibration dialog), best-guess default target channel
/// per index (used by <c>MainWindowViewModel.GuessBestTargetChannel</c> when a new binding is
/// captured), and the full one-click preset used by the Auto-Map Wizard and the per-device
/// "Output As" switch (<see cref="GenerateAutoBindPreset"/>/<see cref="ApplyPreset"/>). Physical index
/// numbering follows SharpDX/DirectInput convention: buttons 0-31 are the device's ordinary
/// buttons, and 128-131 are a synthetic range <see cref="InputDeviceManager"/> assigns to a
/// POV hat's Up/Right/Down/Left when one is present (there's no such thing as "button 128" on
/// real hardware).
/// </summary>
public static class DevicePresetService
{
    /// <summary>
    /// Which raw <see cref="RawDeviceInputState.Axes"/> index drives each analog stick, for the
    /// Visual Device Mapping's live moving-stick graphics - consolidates the same per-category numbering
    /// already implicit in <see cref="GetAxisDisplayName"/> (PlayStation/Nintendo: right stick is
    /// Z/RotationZ, not RotationX/RotationY) rather than re-deriving it from display strings.
    /// Wheel/pedal categories have no sticks; the Xbox/Generic branch is also the fallback for
    /// any unrecognized gamepad, since it's the conventional DirectInput axis ordering.
    /// </summary>
    public static (int leftX, int leftY, int rightX, int rightY) GetStickAxisIndices(string deviceName)
    {
        var category = DetectCategory(deviceName);
        if (IsWheelOrPedalCategory(category)) return (-1, -1, -1, -1);
        if (category is DeviceHardwareCategory.PlayStationController or DeviceHardwareCategory.NintendoController) return (0, 1, 2, 5);
        return (0, 1, 3, 4);
    }

    /// <summary>
    /// Whether one physical axis rests at center with travel in both directions (steering, a
    /// stick axis) versus resting at zero with travel in one direction only (a pedal/trigger/
    /// slider) - for the Input Matrix's live gauge, which has no assigned <c>TargetOutput</c> yet
    /// to string-match against the way <see cref="Models.MappingEntry.IsSteeringAxis"/> and
    /// <see cref="Models.MappingEntry.CalculateCalibratedValue"/>'s <c>isCenteredAxis</c> check
    /// do for an already-mapped entry. Wheel/pedal categories: axis 0 (DirectInput always reports
    /// wheel rotation there) is centered, every other axis is a pedal/lever. Gamepad categories:
    /// whichever axes <see cref="GetStickAxisIndices"/> names as a stick X/Y are centered,
    /// everything else (triggers) is unidirectional.
    /// </summary>
    public static bool IsCenteredAxis(string deviceName, int axisIndex, (int VendorId, int ProductId)? vidPid = null)
    {
        var category = ResolveCategory(deviceName, vidPid);
        if (IsWheelOrPedalCategory(category)) return axisIndex == 0;

        var (lx, ly, rx, ry) = GetStickAxisIndices(deviceName);
        return axisIndex == lx || axisIndex == ly || axisIndex == rx || axisIndex == ry;
    }

    /// <summary>
    /// USB vendor ID -&gt; hardware category, for the handful of call sites that already have a
    /// <see cref="DeviceHardwareInfo"/> (and therefore a decoded VID/PID) in hand - more
    /// reliable than <see cref="DetectCategory"/>'s name-substring matching, which is still the
    /// only option at the many call sites that only have a bare device-name string. Superset of
    /// <see cref="HidHideManager"/>'s own vendor table (kept separate there since it maps to a
    /// display-name string for a different purpose, not a <see cref="DeviceHardwareCategory"/>).
    /// Moza (Gudsen) and Simucube (Granite Devices) VIDs below are NOT independently verified
    /// against real hardware or the USB-IF registry - treat them as best-effort until confirmed;
    /// an unverified/wrong VID here would misclassify an unrelated device, so double check before
    /// relying on them for anything destructive (e.g. Auto-Map preset selection).
    /// </summary>
    private static readonly Dictionary<int, DeviceHardwareCategory> VendorIdToCategory = new()
    {
        [0x054C] = DeviceHardwareCategory.PlayStationController, // Sony
        [0x057E] = DeviceHardwareCategory.NintendoController,    // Nintendo
        [0x045E] = DeviceHardwareCategory.XboxGamepad,           // Microsoft
        [0x046D] = DeviceHardwareCategory.LogitechRig,           // Logitech
        [0x044F] = DeviceHardwareCategory.ThrustmasterRig,       // Thrustmaster
        [0x0EB7] = DeviceHardwareCategory.FanatecRig,            // Fanatec
        [0x1D79] = DeviceHardwareCategory.SimagicRig,            // Simagic
        // Unverified - see doc comment above.
        [0x346E] = DeviceHardwareCategory.MozaWheel,             // Moza / Gudsen (community-reported, unconfirmed)
        [0x16D0] = DeviceHardwareCategory.GenericWheelOrPedals,  // Simucube/Granite Devices *or* Cammus - both community-reported on this VID; can't disambiguate by VID alone, falls back to the generic wheel category rather than guessing one brand over the other
    };

    /// <summary>
    /// Preferred category lookup wherever a caller already has a decoded VID/PID (e.g. from
    /// <see cref="Services.DeviceHardwareInfo"/>) - falls back to the name-substring
    /// <see cref="DetectCategory"/> when the VID isn't in <see cref="VendorIdToCategory"/>.
    /// </summary>
    public static DeviceHardwareCategory DetectCategoryByVidPid(int vendorId, int productId, string fallbackName)
    {
        if (!VendorIdToCategory.TryGetValue(vendorId, out var category)) return DetectCategory(fallbackName);

        // Logitech and Thrustmaster make plenty of gamepads and flight sticks too - the vendor alone must not turn them into wheels.
        if (category is DeviceHardwareCategory.LogitechRig or DeviceHardwareCategory.ThrustmasterRig &&
            NonWheelHint.IsMatch(fallbackName))
            return DeviceHardwareCategory.GenericGamepad;

        return category;
    }

    private static readonly Regex NonWheelHint = new(@"gamepad|f310|f510|f710|rumblepad|dual action|joystick|hotas|flight|extreme 3d|attack 3|warthog|t\.?16000|x5[26]|x-?56|stick", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Public entry point for <see cref="ResolveCategoryAndProfile"/> for callers that only need the resolved category (VID/PID- and <c>wheel_profiles.json</c>-aware), not the profile override itself.</summary>
    public static DeviceHardwareCategory ResolveCategory(string deviceName, (int VendorId, int ProductId)? vidPid = null) =>
        ResolveCategoryAndProfile(deviceName, vidPid).Category;

    /// <summary>
    /// The single category-resolution path <see cref="GetButtonDisplayName"/>/<see cref="GetAxisDisplayName"/>
    /// route through: an optional <see cref="DeviceMetadataLookupService"/> override - an online
    /// registry lookup (Tier 1) falling back to <see cref="WheelProfileService"/>'s bundled/user
    /// <c>wheel_profiles.json</c> (Tier 2) - wins outright if it names a category for this
    /// VID/PID; otherwise prefers
    /// <see cref="DetectCategoryByVidPid"/> when a non-zero VID/PID is available (more reliable
    /// than a name string a driver might garble or rebrand), falling back to
    /// <see cref="DetectCategory"/>'s name-substring matching when it isn't. Also returns the
    /// resolved <see cref="WheelProfileEntry"/> (if any) so callers can check its per-index
    /// button/axis label overrides without a second lookup.
    /// </summary>
    private static (DeviceHardwareCategory Category, WheelProfileEntry? Profile) ResolveCategoryAndProfile(string deviceName, (int VendorId, int ProductId)? vidPid)
    {
        WheelProfileEntry? profile = null;
        if (vidPid is { VendorId: not 0 } vp && DeviceMetadataLookupService.TryResolve(vp.VendorId, vp.ProductId, out var resolved))
            profile = resolved;

        if (profile?.Category != null)
            return (ToHardwareCategory(profile.Category.Value), profile);

        var category = vidPid is { VendorId: not 0 } vidPidValue
            ? DetectCategoryByVidPid(vidPidValue.VendorId, vidPidValue.ProductId, deviceName)
            : DetectCategory(deviceName);
        return (category, profile);
    }

    private static DeviceHardwareCategory ToHardwareCategory(DeviceHardwareCategoryDto dto) => dto switch
    {
        DeviceHardwareCategoryDto.MozaWheel => DeviceHardwareCategory.MozaWheel,
        DeviceHardwareCategoryDto.LogitechRig => DeviceHardwareCategory.LogitechRig,
        DeviceHardwareCategoryDto.FanatecRig => DeviceHardwareCategory.FanatecRig,
        DeviceHardwareCategoryDto.ThrustmasterRig => DeviceHardwareCategory.ThrustmasterRig,
        DeviceHardwareCategoryDto.SimagicRig => DeviceHardwareCategory.SimagicRig,
        DeviceHardwareCategoryDto.GenericWheelOrPedals => DeviceHardwareCategory.GenericWheelOrPedals,
        DeviceHardwareCategoryDto.PlayStationController => DeviceHardwareCategory.PlayStationController,
        DeviceHardwareCategoryDto.NintendoController => DeviceHardwareCategory.NintendoController,
        DeviceHardwareCategoryDto.XboxGamepad => DeviceHardwareCategory.XboxGamepad,
        DeviceHardwareCategoryDto.GenericGamepad => DeviceHardwareCategory.GenericGamepad,
        _ => DeviceHardwareCategory.GenericGamepad
    };

    public static bool IsWheelOrPedalCategory(DeviceHardwareCategory category) =>
        category is DeviceHardwareCategory.MozaWheel or
                     DeviceHardwareCategory.LogitechRig or DeviceHardwareCategory.FanatecRig or
                     DeviceHardwareCategory.ThrustmasterRig or DeviceHardwareCategory.SimagicRig or
                     DeviceHardwareCategory.GenericWheelOrPedals;

    /// <summary>Matches Moza's numeric model names (R3/R5/R9/R12/R16/R21) as whole tokens only - a bare <c>.Contains("r3")</c>/<c>"r9"</c> etc. would false-positive against unrelated device name substrings.</summary>
    private static readonly Regex MozaModelNumber = new(@"\br(3|5|9|12|16|21)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DeviceHardwareCategory DetectCategory(string deviceName)
    {
        string dev = deviceName.ToLowerInvariant();

        if (dev.Contains("dualsense") || dev.Contains("dualshock") || dev.Contains("sony") || dev.Contains("wireless controller") || dev.Contains("ps5") || dev.Contains("ps4") || dev.Contains("ps3"))
            return DeviceHardwareCategory.PlayStationController;

        if (dev.Contains("moza") || dev.Contains("gudsen") || dev.Contains("esx") || MozaModelNumber.IsMatch(dev) ||
            dev.Contains("es steering") || dev.Contains("ks steering") || dev.Contains("fsr") || dev.Contains("cs steering") || dev.Contains("gs steering"))
            return DeviceHardwareCategory.MozaWheel;

        if (dev.Contains("g29") || dev.Contains("g920") || dev.Contains("g923") || dev.Contains("g27") ||
            dev.Contains("g25") || dev.Contains("logitech") || dev.Contains("driving force") || dev.Contains("pro racing wheel"))
            return DeviceHardwareCategory.LogitechRig;

        if (dev.Contains("fanatec") || dev.Contains("csl") || dev.Contains("podium") || dev.Contains("clubsport") || dev.Contains("gran turismo dd"))
            return DeviceHardwareCategory.FanatecRig;

        if (dev.Contains("thrustmaster") || dev.Contains("t300") || dev.Contains("t248") || dev.Contains("t150") ||
            dev.Contains("t500") || dev.Contains("tx") || dev.Contains("t-gt") || dev.Contains("ts-pc") || dev.Contains("t818") || dev.Contains("t128"))
            return DeviceHardwareCategory.ThrustmasterRig;

        if (dev.Contains("simagic") || dev.Contains("alpha") || dev.Contains("p1000") || dev.Contains("p2000") || dev.Contains("gt neo"))
            return DeviceHardwareCategory.SimagicRig;

        if (dev.Contains("wheel") || dev.Contains("pedal") || dev.Contains("shifter") || dev.Contains("handbrake") ||
            dev.Contains("simucube") || dev.Contains("cammus") || dev.Contains("asetek") || dev.Contains("heusinkveld") || dev.Contains("vrs"))
            return DeviceHardwareCategory.GenericWheelOrPedals;

        if (dev.Contains("switch") || dev.Contains("pro controller") || dev.Contains("joy-con") || dev.Contains("nintendo"))
            return DeviceHardwareCategory.NintendoController;

        if (dev.Contains("xbox") || dev.Contains("x-box") || dev.Contains("microsoft") || dev.Contains("xinput") || dev.Contains("8bitdo"))
            return DeviceHardwareCategory.XboxGamepad;

        return DeviceHardwareCategory.GenericGamepad;
    }

    /// <summary>
    /// Best-effort human-readable label for a physical button index, given the device's
    /// detected category. Falls through to a generic "Button N" for gamepad categories, where
    /// the HID usage table is standardized enough (A/B/X/Y, bumpers, D-Pad, ...) for a fixed
    /// per-index table to actually hold across hardware. Wheel/rig categories get no such
    /// table: which raw index is a paddle, a rotary click, or a menu button shifts every time a
    /// rim is swapped (Moza ES vs. KS vs. FSR, Fanatec's various formula/round rims, ...), so
    /// guessing a vehicle function from the index alone is wrong as often as it's right - every
    /// wheel/rig category instead gets the strict "Wheel Button N" (1-indexed) generic label,
    /// with the synthetic POV-hat indices (128-131) still named as D-Pad directions since that
    /// numbering is a structural fact of the hat, not a functional guess. <paramref name="vidPid"/>/
    /// <paramref name="sdkProvider"/> are an optional seam for a future real vendor SDK
    /// integration (see <see cref="IWheelSdkProvider"/>) - when both are supplied and the
    /// provider recognizes the device/index, its native name wins over the generic label below.
    /// Neither is passed by any call site today (no real provider exists yet), so omitting them
    /// preserves exactly today's behavior.
    /// </summary>
    public static string GetButtonDisplayName(string deviceName, int index, (int VendorId, int ProductId)? vidPid = null, IWheelSdkProvider? sdkProvider = null)
    {
        if (vidPid != null && sdkProvider != null &&
            sdkProvider.TryGetNativeButtonName(vidPid.Value.VendorId, vidPid.Value.ProductId, index, out var nativeName) &&
            !string.IsNullOrWhiteSpace(nativeName))
        {
            return nativeName!;
        }

        var (category, profile) = ResolveCategoryAndProfile(deviceName, vidPid);

        if (profile?.Buttons != null && profile.Buttons.TryGetValue(index.ToString(), out var buttonOverride) && !string.IsNullOrWhiteSpace(buttonOverride))
            return buttonOverride;

        switch (category)
        {
            case DeviceHardwareCategory.PlayStationController:
                return index switch
                {
                    0 => "Square (□)",
                    1 => "Cross (✕)",
                    2 => "Circle (○)",
                    3 => "Triangle (△)",
                    4 => "L1 Bumper",
                    5 => "R1 Bumper",
                    6 => "L2 Trigger Button",
                    7 => "R2 Trigger Button",
                    8 => "Create / Share",
                    9 => "Options / Menu",
                    10 => "L3 Stick Click",
                    11 => "R3 Stick Click",
                    12 => "PS Guide Button",
                    13 => "Touchpad Click (Full)",
                    128 => "D-Pad Up",
                    129 => "D-Pad Right",
                    130 => "D-Pad Down",
                    131 => "D-Pad Left",
                    133 => "Touchpad Left Click",
                    134 => "Touchpad Right Click",
                    135 => "Touchpad Top Click",
                    _ => $"Button {index + 1}"
                };

            case DeviceHardwareCategory.NintendoController:
                return index switch
                {
                    0 => "B Button",
                    1 => "A Button",
                    2 => "Y Button",
                    3 => "X Button",
                    4 => "L Bumper",
                    5 => "R Bumper",
                    6 => "ZL Trigger",
                    7 => "ZR Trigger",
                    8 => "Minus (-) Button",
                    9 => "Plus (+) Button",
                    10 => "Left Stick Click",
                    11 => "Right Stick Click",
                    12 => "Home Button",
                    13 => "Capture Button",
                    128 => "D-Pad Up",
                    129 => "D-Pad Right",
                    130 => "D-Pad Down",
                    131 => "D-Pad Left",
                    _ => $"Button {index + 1}"
                };

            case DeviceHardwareCategory.MozaWheel:
            case DeviceHardwareCategory.LogitechRig:
            case DeviceHardwareCategory.FanatecRig:
            case DeviceHardwareCategory.ThrustmasterRig:
            case DeviceHardwareCategory.SimagicRig:
            case DeviceHardwareCategory.GenericWheelOrPedals:
                return index switch
                {
                    128 => "Wheel D-Pad Up",
                    129 => "Wheel D-Pad Right",
                    130 => "Wheel D-Pad Down",
                    131 => "Wheel D-Pad Left",
                    _ => $"Wheel Button {index + 1}"
                };

            case DeviceHardwareCategory.XboxGamepad:
            case DeviceHardwareCategory.GenericGamepad:
            default:
                return index switch
                {
                    0 => "A Button",
                    1 => "B Button",
                    2 => "X Button",
                    3 => "Y Button",
                    4 => "LB Bumper",
                    5 => "RB Bumper",
                    6 => "View / Back",
                    7 => "Menu / Start",
                    8 => "Left Stick Click (LSB)",
                    9 => "Right Stick Click (RSB)",
                    10 => "Xbox Guide Button",
                    128 => "D-Pad Up",
                    129 => "D-Pad Right",
                    130 => "D-Pad Down",
                    131 => "D-Pad Left",
                    _ => $"Button {index + 1}"
                };
        }
    }

    /// <summary>Fixed DirectInput axis order every raw axis array in this app follows (see <see cref="RawDeviceInputState.Axes"/>/<see cref="InputDeviceManager"/>'s polling loop) - X, Y, Z, RotationX, RotationY, RotationZ, Slider0, Slider1. Used as the generic, brand-independent axis label for wheel/rig categories (see <see cref="GetAxisDisplayName"/>): which physical pedal/lever actually drives a given index varies by pedal set and can't be guessed from the index alone.</summary>
    private static readonly string[] GenericAxisNames =
    [
        "Axis X", "Axis Y", "Axis Z", "Rotation X", "Rotation Y", "Rotation Z", "Slider 0", "Slider 1"
    ];

    /// <summary>Same idea as <see cref="GetButtonDisplayName"/>, but for the 6-8 element raw axis array (see <see cref="RawDeviceInputState.Axes"/>). <paramref name="vidPid"/>/<paramref name="sdkProvider"/> are the same optional future-SDK seam.</summary>
    public static string GetAxisDisplayName(string deviceName, int index, (int VendorId, int ProductId)? vidPid = null, IWheelSdkProvider? sdkProvider = null)
    {
        if (vidPid != null && sdkProvider != null &&
            sdkProvider.TryGetNativeAxisName(vidPid.Value.VendorId, vidPid.Value.ProductId, index, out var nativeName) &&
            !string.IsNullOrWhiteSpace(nativeName))
        {
            return nativeName!;
        }

        var (category, profile) = ResolveCategoryAndProfile(deviceName, vidPid);

        if (profile?.Axes != null && profile.Axes.TryGetValue(index.ToString(), out var axisOverride) && !string.IsNullOrWhiteSpace(axisOverride))
            return axisOverride;

        switch (category)
        {
            case DeviceHardwareCategory.PlayStationController:
            case DeviceHardwareCategory.NintendoController:
                return index switch
                {
                    0 => "Left Stick Horizontal",
                    1 => "Left Stick Vertical",
                    2 => "Right Stick Horizontal",
                    3 => "L2 Trigger Axis",
                    4 => "R2 Trigger Axis",
                    5 => "Right Stick Vertical",
                    _ => $"Axis {index + 1}"
                };

            case DeviceHardwareCategory.MozaWheel:
            case DeviceHardwareCategory.LogitechRig:
            case DeviceHardwareCategory.FanatecRig:
            case DeviceHardwareCategory.ThrustmasterRig:
            case DeviceHardwareCategory.SimagicRig:
            case DeviceHardwareCategory.GenericWheelOrPedals:
                return index >= 0 && index < GenericAxisNames.Length ? GenericAxisNames[index] : $"Axis {index + 1}";

            case DeviceHardwareCategory.XboxGamepad:
            case DeviceHardwareCategory.GenericGamepad:
            default:
                return index switch
                {
                    0 => "Left Stick X",
                    1 => "Left Stick Y",
                    2 => "Left Trigger (LT)",
                    3 => "Right Stick X",
                    4 => "Right Stick Y",
                    5 => "Right Trigger (RT)",
                    _ => $"Axis {index + 1}"
                };
        }
    }

    /// <summary>
    /// Maps every "[Xbox] ..." target string this file's presets can emit to its "[DS4] ..."
    /// functional equivalent (see the parallel <c>xboxBinds</c>/<c>ds4Binds</c> arrays in
    /// <c>MainWindowViewModel.BuildAvailableVirtualOutputs</c>, which this table's values must
    /// stay in sync with). Used by <see cref="ForCategory"/> so every preset branch below only
    /// has to spell out its Xbox and Wheel strings - the DS4 string is derived, not duplicated.
    /// </summary>
    private static readonly Dictionary<string, string> XboxToDs4Target = new()
    {
        ["[Xbox] Left Stick X (Steer / Horizontal)"] = "[DS4] Left Stick X (Steer / Horizontal)",
        ["[Xbox] Left Stick Y (Steer / Vertical)"] = "[DS4] Left Stick Y (Steer / Vertical)",
        ["[Xbox] Right Stick X (Camera / Look Horizontal)"] = "[DS4] Right Stick X (Camera / Look Horizontal)",
        ["[Xbox] Right Stick Y (Camera / Look Vertical)"] = "[DS4] Right Stick Y (Camera / Look Vertical)",
        ["[Xbox] Left Trigger (LT / Brake Axis)"] = "[DS4] L2 Trigger (Brake Axis)",
        ["[Xbox] Right Trigger (RT / Gas Axis)"] = "[DS4] R2 Trigger (Gas Axis)",
        ["[Xbox] Xbox A (Cross / South / Handbrake)"] = "[DS4] Cross (X / South)",
        ["[Xbox] Xbox B (Circle / East)"] = "[DS4] Circle (O / East)",
        ["[Xbox] Xbox X (Square / West / Shift Down)"] = "[DS4] Square (Sq / West)",
        ["[Xbox] Xbox Y (Triangle / North / Shift Up)"] = "[DS4] Triangle (Tri / North)",
        ["[Xbox] Xbox LB (Left Bumper / L1 / Clutch)"] = "[DS4] L1 (Left Bumper / Clutch)",
        ["[Xbox] Xbox RB (Right Bumper / R1)"] = "[DS4] R1 (Right Bumper)",
        ["[Xbox] Xbox LSB (Left Stick Click / L3)"] = "[DS4] L3 (Left Stick Click)",
        ["[Xbox] Xbox RSB (Right Stick Click / R3)"] = "[DS4] R3 (Right Stick Click)",
        ["[Xbox] D-Pad Up"] = "[DS4] D-Pad Up",
        ["[Xbox] D-Pad Down"] = "[DS4] D-Pad Down",
        ["[Xbox] D-Pad Left"] = "[DS4] D-Pad Left",
        ["[Xbox] D-Pad Right"] = "[DS4] D-Pad Right",
        ["[Xbox] Xbox Menu (Start / Options)"] = "[DS4] Options (Start)",
        ["[Xbox] Xbox View (Back / Map / Share)"] = "[DS4] Share (Back)",
        ["[Xbox] Xbox Guide (Home / Guide)"] = "[DS4] PS (Guide)",
    };

    /// <summary>
    /// Picks the right target string for <paramref name="category"/> given a branch's Xbox and
    /// Wheel strings - Wheel returns as-is, Xbox returns as-is, DualShock4 looks up the Xbox
    /// string's equivalent in <see cref="XboxToDs4Target"/> (falling back to the Xbox string
    /// itself if a new Xbox target is ever added here without a matching DS4 entry, so a preset
    /// still produces a *some* target rather than an empty one).
    /// </summary>
    private static string ForCategory(OutputCategory category, string xboxTarget, string wheelTarget) => category switch
    {
        OutputCategory.Wheel => wheelTarget,
        OutputCategory.DualShock4 => XboxToDs4Target.TryGetValue(xboxTarget, out var ds4) ? ds4 : xboxTarget,
        _ => xboxTarget
    };

    /// <summary>
    /// Drops preset entries whose physical index the connected device doesn't actually report -
    /// <see cref="GenerateAutoBindPreset"/>'s per-category tables are fixed-length and were previously
    /// applied unconditionally regardless of what the device's real <c>ButtonCount</c>/<c>AxisCount</c>
    /// (see <see cref="InputDeviceManager.GetConnectedDevices"/>) said, producing mappings for
    /// buttons/axes that don't exist. POV-hat synthetic indices (128-131) are kept only when
    /// <paramref name="hasPov"/> is true.
    /// </summary>
    private static List<PresetMappingItem> FilterToCapabilities(List<PresetMappingItem> list, int buttonCount, int axisSlotMask, bool hasPov) =>
        list.Where(i => i.Type == InputType.Axis
            ? i.PhysicalIndex is >= 0 and < 8 && (axisSlotMask & (1 << i.PhysicalIndex)) != 0
            : (i.PhysicalIndex < buttonCount || (i.PhysicalIndex is >= 128 and <= 131 && hasPov))
        ).ToList();

    /// <summary>
    /// Common tail for every <see cref="GenerateAutoBindPreset"/> branch: filters to what the device
    /// actually reports (<see cref="FilterToCapabilities"/>), then for Wheel output only, adds a
    /// generic fallback binding for any real button beyond the hardcoded per-category table
    /// (e.g. a 20-button wheel whose brand table above only names buttons 0-18) so extra buttons
    /// aren't silently dropped - targeted at the already-provisioned "[Wheel] Button N" (1-32)
    /// vocabulary in <c>MainWindowViewModel.BuildAvailableVirtualOutputs</c>, keyed by the
    /// button's own 1-based index. Xbox/DS4 output intentionally leaves extra buttons unmapped -
    /// a 14-17-button virtual pad has no sensible destination for a 20th physical button.
    /// </summary>
    private static List<PresetMappingItem> FinalizePreset(List<PresetMappingItem> list, string deviceName, int buttonCount, int axisSlotMask, bool hasPov, OutputCategory outputCategory, (int VendorId, int ProductId)? vidPid = null)
    {
        var filtered = FilterToCapabilities(list, buttonCount, axisSlotMask, hasPov);

        if (outputCategory == OutputCategory.Wheel)
        {
            var covered = filtered.Where(i => i.Type == InputType.Button && i.PhysicalIndex is >= 0 and < 128)
                                   .Select(i => i.PhysicalIndex).ToHashSet();

            for (int i = 0; i < buttonCount; i++)
            {
                if (covered.Contains(i)) continue;
                if (i + 1 > 32) break; // only 32 "[Wheel] Button N" slots exist

                filtered.Add(new PresetMappingItem
                {
                    PhysicalName = GetButtonDisplayName(deviceName, i, vidPid),
                    Type = InputType.Button,
                    PhysicalIndex = i,
                    DefaultTargetOutput = $"[Wheel] Button {i + 1}"
                });
            }
        }

        return filtered;
    }

    // ---- Auto Bind (Xbox 360 / DirectInput / Combined) -------------------------------------------

    /// <summary>What the layout resolver learned about one device - everything <see cref="GenerateAutoBindPreset"/> needs to place its buttons and axes.</summary>
    private sealed record AutoBindLayout(
        DeviceHardwareCategory Category,
        bool IsWheel,
        WheelProfileEntry? Profile,
        Dictionary<int, string> XboxButtons,
        string Source);

    /// <summary>Standard Xbox-over-DirectInput button order (what Windows reports for any XInput-class pad). The Guide button is not exposed over DirectInput.</summary>
    private static Dictionary<int, string> StandardXboxButtons() => new()
    {
        [0] = XboxButtonNames.A, [1] = XboxButtonNames.B, [2] = XboxButtonNames.X, [3] = XboxButtonNames.Y,
        [4] = XboxButtonNames.LB, [5] = XboxButtonNames.RB, [6] = XboxButtonNames.View, [7] = XboxButtonNames.Menu,
        [8] = XboxButtonNames.LSB, [9] = XboxButtonNames.RSB
    };

    private static Dictionary<int, string> PlayStationXboxButtons() => new()
    {
        [1] = XboxButtonNames.A, [2] = XboxButtonNames.B, [0] = XboxButtonNames.X, [3] = XboxButtonNames.Y,
        [4] = XboxButtonNames.LB, [5] = XboxButtonNames.RB, [8] = XboxButtonNames.View, [9] = XboxButtonNames.Menu,
        [10] = XboxButtonNames.LSB, [11] = XboxButtonNames.RSB, [12] = XboxButtonNames.Guide
    };

    /// <summary>
    /// Works out which buttons on this device are Xbox buttons, in priority order: an explicit
    /// <c>xboxButtons</c> table in <c>wheel_profiles.json</c>; the standard Xbox order when the
    /// device is also visible to XInput; the PlayStation table; the standard order for plain
    /// gamepads. A wheel that matches none of these gets no Xbox buttons (they stay generic
    /// "Button N") - guessing a face button from a wheel's raw index is wrong as often as right.
    /// </summary>
    private static AutoBindLayout ResolveAutoBindLayout(string deviceName, (int VendorId, int ProductId)? vidPid, bool exposesXInput)
    {
        var (category, profile) = ResolveCategoryAndProfile(deviceName, vidPid);
        bool isWheel = IsWheelOrPedalCategory(category);
        exposesXInput = profile?.ExposesXInput ?? exposesXInput;

        Dictionary<int, string> map;
        string source;

        if (profile?.XboxButtons is { Count: > 0 } explicitMap)
        {
            map = new Dictionary<int, string>();
            foreach (var (key, value) in explicitMap)
            {
                if (int.TryParse(key, out int index) && XboxButtonNames.TryGetTarget(value, out _)) map[index] = value;
            }
            source = "wheel_profiles.json layout";
        }
        else if (exposesXInput)
        {
            map = StandardXboxButtons();
            source = "XInput device (standard Xbox button order)";
        }
        else if (category == DeviceHardwareCategory.PlayStationController)
        {
            map = PlayStationXboxButtons();
            source = "PlayStation controller layout";
        }
        else if (!isWheel)
        {
            map = StandardXboxButtons();
            source = category == DeviceHardwareCategory.XboxGamepad ? "Xbox gamepad layout" : "Generic gamepad (assumed standard Xbox order)";
        }
        else
        {
            map = new Dictionary<int, string>();
            source = "Wheel with no known Xbox button layout (buttons stay generic)";
        }

        return new AutoBindLayout(category, isWheel, profile, map, source);
    }

    private static readonly (int Slot, string Role)[] DefaultWheelAxisRoles =
    [
        (0, "Steering"), (1, "Gas"), (2, "Brake"), (3, "Clutch"), (4, "Handbrake")
    ];

    private static string WheelAxisTarget(string role) => role switch
    {
        "Steering" => "[Wheel] Steering (Axis X)",
        "Gas" => "[Wheel] Gas / Throttle (Axis Y)",
        "Brake" => "[Wheel] Brake (Axis Z)",
        "Clutch" => "[Wheel] Clutch (Axis Rx)",
        "Handbrake" => "[Wheel] Handbrake (Axis Ry)",
        "RightSlider" => "[Wheel] Combined Slider 0",
        _ => string.Empty
    };

    private const string XboxLsx = "[Xbox] Left Stick X (Steer / Horizontal)";
    private const string XboxLsy = "[Xbox] Left Stick Y (Steer / Vertical)";
    private const string XboxRsx = "[Xbox] Right Stick X (Camera / Look Horizontal)";
    private const string XboxRsy = "[Xbox] Right Stick Y (Camera / Look Vertical)";
    private const string XboxLt = "[Xbox] Left Trigger (LT / Brake Axis)";
    private const string XboxRt = "[Xbox] Right Trigger (RT / Gas Axis)";

    /// <summary>
    /// Builds the Auto Map preset for one device in one <see cref="AutoBindMode"/>:
    /// <list type="bullet">
    /// <item><b>Xbox360</b> - everything recognised goes to the virtual Xbox 360 pad.</item>
    /// <item><b>DualShock4</b> - same, translated to the DS4 targets.</item>
    /// <item><b>DirectInput</b> - generic wheel layout on a vJoy slot: axes by role, every button as "Button N".</item>
    /// <item><b>Combined</b> - Xbox buttons (and the D-pad) go to the Xbox pad, the wheel's steering/pedals/clutch/handbrake go to the vJoy wheel, any other button is "Button N" on the wheel.</item>
    /// </list>
    /// Nothing is guessed from the vendor alone: axis slots must actually exist (<paramref name="axisSlotMask"/>), and buttons only reach an Xbox target when the layout resolver can vouch for them.
    /// </summary>
    public static AutoBindResult GenerateAutoBindPreset(string deviceName, int buttonCount, int axisSlotMask, bool hasPov, AutoBindMode mode, (int VendorId, int ProductId)? vidPid = null, bool exposesXInput = false)
    {
        var layout = ResolveAutoBindLayout(deviceName, vidPid, exposesXInput);
        var items = new List<PresetMappingItem>();

        bool hasSlot(int slot) => slot >= 0 && slot < 8 && (axisSlotMask & (1 << slot)) != 0;
        string xbox(string target) => mode == AutoBindMode.DualShock4 && XboxToDs4Target.TryGetValue(target, out var ds4) ? ds4 : target;
        bool wheelAxes = mode is AutoBindMode.DirectInput or AutoBindMode.Combined;

        void addAxis(int slot, string target, bool inverted = false, int rawMin = 0, int rawMax = 65535, string? nameSuffix = null)
        {
            if (string.IsNullOrEmpty(target) || !hasSlot(slot)) return;
            string name = GetAxisDisplayName(deviceName, slot, vidPid);
            items.Add(new PresetMappingItem
            {
                PhysicalName = nameSuffix == null ? name : $"{name} ({nameSuffix})",
                Type = InputType.Axis,
                PhysicalIndex = slot,
                DefaultTargetOutput = target,
                IsInverted = inverted,
                RawMin = rawMin,
                RawMax = rawMax
            });
        }

        // ---- axes ----
        if (layout.IsWheel)
        {
            var roles = new Dictionary<int, string>();
            if (layout.Profile?.AxisRoles is { Count: > 0 } custom)
            {
                foreach (var (key, role) in custom)
                    if (int.TryParse(key, out int slot)) roles[slot] = role;
            }
            else
            {
                foreach (var (slot, role) in DefaultWheelAxisRoles) roles[slot] = role;
            }

            foreach (var (slot, role) in roles.OrderBy(r => r.Key))
            {
                if (role.Equals("Ignore", StringComparison.OrdinalIgnoreCase)) continue;
                bool isPedal = role is "Gas" or "Brake" or "Clutch" or "Handbrake";
                // Pedals usually rest at max travel. A profile that lists InvertedAxes is authoritative; without one, assume inverted.
                bool inverted = isPedal && (layout.Profile?.InvertedAxes?.Contains(slot) ?? true);

                string target;
                if (wheelAxes) target = WheelAxisTarget(role);
                else target = xbox(role switch { "Steering" => XboxLsx, "Gas" => XboxRt, "Brake" => XboxLt, _ => string.Empty });

                addAxis(slot, target, inverted);
            }
        }
        else
        {
            bool xinputStyle = exposesXInput || layout.Category == DeviceHardwareCategory.XboxGamepad;
            bool ps = layout.Category == DeviceHardwareCategory.PlayStationController;

            // Per-layout axis slots: LX, LY, RX, RY, LT, RT. A combined trigger (XInput over DirectInput reports both on Z) is split below.
            int lx = 0, ly = 1, rx, ry, lt, rt, combined = -1;
            if (ps) { rx = 2; ry = 5; lt = 3; rt = 4; }
            else if (xinputStyle) { rx = 3; ry = 4; lt = -1; rt = -1; combined = 2; }
            else { rx = 3; ry = 4; lt = 2; rt = 5; }

            if (wheelAxes)
            {
                addAxis(lx, WheelAxisTarget("Steering"));
                addAxis(lt, WheelAxisTarget("Brake"), false);
                addAxis(rt, WheelAxisTarget("Gas"), false);
                addAxis(rx, WheelAxisTarget("Clutch"));
                addAxis(ry, WheelAxisTarget("Handbrake"));
                if (combined >= 0)
                {
                    addAxis(combined, WheelAxisTarget("Gas"), true, 0, 32768, "RT half");
                    addAxis(combined, WheelAxisTarget("Brake"), false, 32768, 65535, "LT half");
                }
            }
            else
            {
                addAxis(lx, xbox(XboxLsx));
                addAxis(ly, xbox(XboxLsy), true);
                addAxis(rx, xbox(XboxRsx));
                addAxis(ry, xbox(XboxRsy), true);
                addAxis(lt, xbox(XboxLt));
                addAxis(rt, xbox(XboxRt));
                if (combined >= 0)
                {
                    // Z rests at the midpoint: LT pulls it toward max, RT toward min.
                    addAxis(combined, xbox(XboxRt), true, 0, 32768, "RT half");
                    addAxis(combined, xbox(XboxLt), false, 32768, 65535, "LT half");
                }
            }
        }

        // ---- buttons ----
        bool xboxButtons = mode != AutoBindMode.DirectInput;
        var claimed = new HashSet<int>();

        if (xboxButtons)
        {
            foreach (var (index, name) in layout.XboxButtons.OrderBy(b => b.Key))
            {
                if (index >= buttonCount || !XboxButtonNames.TryGetTarget(name, out var target)) continue;
                items.Add(new PresetMappingItem
                {
                    PhysicalName = $"{GetButtonDisplayName(deviceName, index, vidPid)} ({name})",
                    Type = InputType.Button,
                    PhysicalIndex = index,
                    DefaultTargetOutput = xbox(target)
                });
                claimed.Add(index);
            }
        }

        if (hasPov)
        {
            // The hat is a structural D-pad, so it needs no layout table.
            (int Index, string Name)[] dpad =
            [
                (128, XboxButtonNames.DpadUp), (129, XboxButtonNames.DpadRight), (130, XboxButtonNames.DpadDown), (131, XboxButtonNames.DpadLeft)
            ];
            for (int i = 0; i < dpad.Length; i++)
            {
                string target = xboxButtons && XboxButtonNames.TryGetTarget(dpad[i].Name, out var t)
                    ? xbox(t)
                    : $"[Wheel] Button {buttonCount + i + 1}";
                if (!xboxButtons && buttonCount + i + 1 > 32) continue;
                items.Add(new PresetMappingItem
                {
                    PhysicalName = GetButtonDisplayName(deviceName, dpad[i].Index, vidPid),
                    Type = InputType.Button,
                    PhysicalIndex = dpad[i].Index,
                    DefaultTargetOutput = target
                });
            }
        }

        // Every other button: "Button N" on the wheel in DirectInput/Combined, unbound in Xbox/DS4 modes.
        if (wheelAxes)
        {
            for (int i = 0; i < buttonCount && i < 32; i++)
            {
                if (claimed.Contains(i)) continue;
                items.Add(new PresetMappingItem
                {
                    PhysicalName = GetButtonDisplayName(deviceName, i, vidPid),
                    Type = InputType.Button,
                    PhysicalIndex = i,
                    DefaultTargetOutput = $"[Wheel] Button {i + 1}"
                });
            }
        }

        bool hasXboxFaceButtons = layout.XboxButtons.Values.Any(v => v is XboxButtonNames.A or XboxButtonNames.B or XboxButtonNames.X or XboxButtonNames.Y);
        var suggested = !layout.IsWheel
            ? (layout.Category == DeviceHardwareCategory.PlayStationController && mode == AutoBindMode.DualShock4 ? AutoBindMode.DualShock4 : AutoBindMode.Xbox360)
            : hasXboxFaceButtons ? AutoBindMode.Combined : AutoBindMode.DirectInput;

        return new AutoBindResult(items, layout.Source, suggested);
    }

    /// <summary>The mode Auto Map should pre-select for a device: Combined for a wheel with Xbox buttons, DirectInput for a wheel without, Xbox 360 for a gamepad.</summary>
    public static AutoBindMode SuggestAutoBindMode(string deviceName, (int VendorId, int ProductId)? vidPid, bool exposesXInput) =>
        GenerateAutoBindPreset(deviceName, 32, 0xFF, true, AutoBindMode.Xbox360, vidPid, exposesXInput).SuggestedMode;

    /// <summary>
    /// The Input Matrix tab's "Auto-Map (1:1)" preset: strict index-for-index identity, no
    /// per-category guessing (unlike every branch of <see cref="GenerateAutoBindPreset"/> above, which
    /// picks functional button identities - Cross/Circle/A/B/etc. - per hardware category). Every
    /// physical button 0..buttonCount-1 becomes "[Wheel] Button {i+1}" via <see cref="FinalizePreset"/>'s
    /// existing gap-filler (there's no per-category button table here for it to skip - the list
    /// starts with only axes and POV in it), and axes 0-4 map to the same brand-independent wheel
    /// targets <see cref="GenerateAutoBindPreset"/>'s wheel-category branch already uses for a steering
    /// wheel (rotation is always axis X; which further axis is which pedal isn't guessable, but
    /// giving each its own identity slot lets the user confirm/relabel via the matrix itself).
    /// </summary>
    public static List<PresetMappingItem> GenerateStrict1To1Preset(string deviceName, int buttonCount, int axisCount, bool hasPov, (int VendorId, int ProductId)? vidPid = null, int axisSlotMask = -1)
    {
        if (axisSlotMask < 0) axisSlotMask = (1 << Math.Clamp(axisCount, 0, 8)) - 1;
        var list = new List<PresetMappingItem>();

        string[] axisTargets =
        [
            "[Wheel] Steering (Axis X)", "[Wheel] Gas / Throttle (Axis Y)", "[Wheel] Brake (Axis Z)",
            "[Wheel] Clutch (Axis Rx)", "[Wheel] Handbrake (Axis Ry)"
        ];
        for (int i = 0; i < axisTargets.Length; i++)
        {
            if ((axisSlotMask & (1 << i)) == 0) continue;
            list.Add(new PresetMappingItem { PhysicalName = GetAxisDisplayName(deviceName, i, vidPid), Type = InputType.Axis, PhysicalIndex = i, DefaultTargetOutput = axisTargets[i] });
        }

        if (hasPov)
        {
            // Same Up/Down/Left/Right -> sequential-button-number convention GenerateAutoBindPreset's
            // wheel branch already uses, reused verbatim for consistency rather than inventing a
            // different POV numbering here.
            list.Add(new PresetMappingItem { PhysicalName = GetButtonDisplayName(deviceName, 128, vidPid), Type = InputType.Button, PhysicalIndex = 128, DefaultTargetOutput = $"[Wheel] Button {buttonCount + 1}" });
            list.Add(new PresetMappingItem { PhysicalName = GetButtonDisplayName(deviceName, 130, vidPid), Type = InputType.Button, PhysicalIndex = 130, DefaultTargetOutput = $"[Wheel] Button {buttonCount + 2}" });
            list.Add(new PresetMappingItem { PhysicalName = GetButtonDisplayName(deviceName, 131, vidPid), Type = InputType.Button, PhysicalIndex = 131, DefaultTargetOutput = $"[Wheel] Button {buttonCount + 3}" });
            list.Add(new PresetMappingItem { PhysicalName = GetButtonDisplayName(deviceName, 129, vidPid), Type = InputType.Button, PhysicalIndex = 129, DefaultTargetOutput = $"[Wheel] Button {buttonCount + 4}" });
        }

        // Every ordinary button 0..buttonCount-1 gets its "[Wheel] Button N" identity binding
        // from FinalizePreset's gap-filler (only runs for OutputCategory.Wheel) - nothing to add
        // here since there's no per-category button table to special-case around.
        return FinalizePreset(list, deviceName, buttonCount, axisSlotMask, hasPov, OutputCategory.Wheel, vidPid);
    }

    /// <summary>
    /// Applies a generated preset to the shared mapping list for one device. An item whose physical
    /// slot already has a <see cref="MappingEntry"/> only gets its target (and, for entries Auto Map
    /// created itself, inversion and raw window) updated - a hand-made entry keeps its deadzone,
    /// calibration and hotspot link. Other items get a new entry flagged <see cref="MappingEntry.IsAutoBound"/>.
    /// Afterwards, earlier auto-bound entries the new preset no longer covers are removed (so
    /// switching modes leaves no stale bindings); entries the user bound by hand are never removed.
    /// <paramref name="wheelSlot"/> routes "[Wheel]" items to a vJoy slot while the rest use
    /// <paramref name="targetDeviceId"/> (the Xbox/DS4 player) - that is what lets Combined mode feed two
    /// virtual devices. Shared by <c>AutoMapWizardViewModel.ApplyWizardMappings</c> and the Input Matrix auto-map.
    /// </summary>
    public static void ApplyPreset(ObservableCollection<MappingEntry> mappings, string deviceName, Guid deviceGuid, List<PresetMappingItem> presetItems, uint targetDeviceId, uint? wheelSlot = null)
    {
        var existingBySlot = mappings
            .Where(m => m.SourceDeviceName == deviceName)
            .GroupBy(m => (m.SourceType, m.SourceIndex))
            .ToDictionary(g => g.Key, g => g.First());

        // Entries this preset touched, and slots already claimed by an earlier item (a split
        // trigger axis puts two items on one slot - the second needs its own entry).
        var touched = new HashSet<MappingEntry>();
        var claimedSlots = new HashSet<(InputType, int)>();

        foreach (var item in presetItems)
        {
            // A "[Wheel]" item goes to the wheel slot, everything else to the player, so one preset can feed both.
            uint itemTarget = wheelSlot.HasValue && item.DefaultTargetOutput.StartsWith("[Wheel]", StringComparison.Ordinal)
                ? wheelSlot.Value
                : targetDeviceId;
            var slotKey = (item.Type, item.PhysicalIndex);

            if (claimedSlots.Add(slotKey) && existingBySlot.TryGetValue(slotKey, out var existing))
            {
                existing.TargetOutput = item.DefaultTargetOutput;
                existing.TargetDeviceId = itemTarget;
                if (existing.IsAutoBound)
                {
                    // Our own earlier output: its calibration came from a previous preset, so refresh it.
                    existing.IsInverted = item.IsInverted;
                    existing.RawMin = item.RawMin;
                    existing.RawMax = item.RawMax;
                }
                touched.Add(existing);
                continue;
            }

            var created = new MappingEntry
            {
                SourceDeviceName = deviceName,
                SourceDeviceGuid = deviceGuid,
                SourceType = item.Type,
                SourceIndex = item.PhysicalIndex,
                SourceDisplayName = item.PhysicalName,
                TargetDeviceId = itemTarget,
                TargetOutput = item.DefaultTargetOutput,
                Deadzone = item.Type == InputType.Axis ? 0.08 : 0.0,
                RawMin = item.RawMin,
                RawMax = item.RawMax,
                IsInverted = item.IsInverted,
                IsAutoBound = true
            };
            mappings.Add(created);
            touched.Add(created);
        }

        // Drop earlier Auto Map output the new preset no longer covers (e.g. after switching mode).
        // Entries the user bound by hand are never removed.
        foreach (var stale in mappings.Where(m => m.SourceDeviceName == deviceName && m.IsAutoBound && !touched.Contains(m)).ToList())
            mappings.Remove(stale);
    }
}
