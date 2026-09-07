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
/// two) so <see cref="DevicePresetService.GeneratePreset"/> and
/// <see cref="ECCR.ViewModels.AutoBindWizardViewModel"/> can express all three real backends.
/// </summary>
public enum OutputCategory
{
    Xbox,
    DualShock4,
    Wheel
}

/// <summary>
/// Everything about mapping a *specific* piece of hardware, keyed primarily by its DirectInput
/// instance name string (matched by substring in <see cref="DetectCategory"/>), with an optional
/// USB VID/PID available at most call sites for the more reliable <see cref="DetectCategoryByVidPid"/>
/// path and the <see cref="WheelProfileService"/> (<c>wheel_profiles.json</c>) override lookup -
/// see <see cref="ResolveCategoryAndProfile"/>, the single path both go through. Three things
/// live here that all depend on that same category detection: friendly display names for raw
/// button/axis indices (<see cref="GetButtonDisplayName"/>/<see cref="GetAxisDisplayName"/>, used
/// by the "Click to Bind" listener and the Calibration dialog), best-guess default target channel
/// per index (used by <c>MainWindowViewModel.GuessBestTargetChannel</c> when a new binding is
/// captured), and the full one-click preset used by the Auto-Bind Wizard and the per-device
/// "Output As" switch (<see cref="GeneratePreset"/>/<see cref="ApplyPreset"/>). Physical index
/// numbering follows SharpDX/DirectInput convention: buttons 0-31 are the device's ordinary
/// buttons, and 128-131 are a synthetic range <see cref="InputDeviceManager"/> assigns to a
/// POV hat's Up/Right/Down/Left when one is present (there's no such thing as "button 128" on
/// real hardware).
/// </summary>
public static class DevicePresetService
{
    /// <summary>
    /// Which raw <see cref="RawDeviceInputState.Axes"/> index drives each analog stick, for the
    /// Visual Binder's live moving-stick graphics - consolidates the same per-category numbering
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
    /// USB vendor ID -&gt; hardware category, for the handful of call sites that already have a
    /// <see cref="DeviceHardwareInfo"/> (and therefore a decoded VID/PID) in hand - more
    /// reliable than <see cref="DetectCategory"/>'s name-substring matching, which is still the
    /// only option at the many call sites that only have a bare device-name string. Superset of
    /// <see cref="HidHideManager"/>'s own vendor table (kept separate there since it maps to a
    /// display-name string for a different purpose, not a <see cref="DeviceHardwareCategory"/>).
    /// Moza (Gudsen) and Simucube (Granite Devices) VIDs below are NOT independently verified
    /// against real hardware or the USB-IF registry - treat them as best-effort until confirmed;
    /// an unverified/wrong VID here would misclassify an unrelated device, so double check before
    /// relying on them for anything destructive (e.g. Auto-Bind preset selection).
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
    public static DeviceHardwareCategory DetectCategoryByVidPid(int vendorId, int productId, string fallbackName) =>
        VendorIdToCategory.TryGetValue(vendorId, out var category) ? category : DetectCategory(fallbackName);

    /// <summary>Public entry point for <see cref="ResolveCategoryAndProfile"/> for callers that only need the resolved category (VID/PID- and <c>wheel_profiles.json</c>-aware), not the profile override itself.</summary>
    public static DeviceHardwareCategory ResolveCategory(string deviceName, (int VendorId, int ProductId)? vidPid = null) =>
        ResolveCategoryAndProfile(deviceName, vidPid).Category;

    /// <summary>
    /// The single category-resolution path <see cref="GetButtonDisplayName"/>/<see cref="GetAxisDisplayName"/>
    /// route through: an optional <see cref="WheelProfileService"/> override (<c>wheel_profiles.json</c>)
    /// wins outright if it names a category for this VID/PID; otherwise prefers
    /// <see cref="DetectCategoryByVidPid"/> when a non-zero VID/PID is available (more reliable
    /// than a name string a driver might garble or rebrand), falling back to
    /// <see cref="DetectCategory"/>'s name-substring matching when it isn't. Also returns the
    /// resolved <see cref="WheelProfileEntry"/> (if any) so callers can check its per-index
    /// button/axis label overrides without a second lookup.
    /// </summary>
    private static (DeviceHardwareCategory Category, WheelProfileEntry? Profile) ResolveCategoryAndProfile(string deviceName, (int VendorId, int ProductId)? vidPid)
    {
        WheelProfileEntry? profile = null;
        if (vidPid is { VendorId: not 0 } vp && WheelProfileService.TryResolve(vp.VendorId, vp.ProductId, out var resolved))
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
    /// <see cref="GeneratePreset"/>'s per-category tables are fixed-length and were previously
    /// applied unconditionally regardless of what the device's real <c>ButtonCount</c>/<c>AxisCount</c>
    /// (see <see cref="InputDeviceManager.GetConnectedDevices"/>) said, producing bindings for
    /// buttons/axes that don't exist. POV-hat synthetic indices (128-131) are kept only when
    /// <paramref name="hasPov"/> is true.
    /// </summary>
    private static List<PresetBindingItem> FilterToCapabilities(List<PresetBindingItem> list, int buttonCount, int axisCount, bool hasPov) =>
        list.Where(i => i.Type == InputType.Axis
            ? i.PhysicalIndex < axisCount
            : (i.PhysicalIndex < buttonCount || (i.PhysicalIndex is >= 128 and <= 131 && hasPov))
        ).ToList();

    /// <summary>
    /// Common tail for every <see cref="GeneratePreset"/> branch: filters to what the device
    /// actually reports (<see cref="FilterToCapabilities"/>), then for Wheel output only, adds a
    /// generic fallback binding for any real button beyond the hardcoded per-category table
    /// (e.g. a 20-button wheel whose brand table above only names buttons 0-18) so extra buttons
    /// aren't silently dropped - targeted at the already-provisioned "[Wheel] Button N" (1-32)
    /// vocabulary in <c>MainWindowViewModel.BuildAvailableVirtualOutputs</c>, keyed by the
    /// button's own 1-based index. Xbox/DS4 output intentionally leaves extra buttons unmapped -
    /// a 14-17-button virtual pad has no sensible destination for a 20th physical button.
    /// </summary>
    private static List<PresetBindingItem> FinalizePreset(List<PresetBindingItem> list, string deviceName, int buttonCount, int axisCount, bool hasPov, OutputCategory outputCategory, (int VendorId, int ProductId)? vidPid = null)
    {
        var filtered = FilterToCapabilities(list, buttonCount, axisCount, hasPov);

        if (outputCategory == OutputCategory.Wheel)
        {
            var covered = filtered.Where(i => i.Type == InputType.Button && i.PhysicalIndex is >= 0 and < 128)
                                   .Select(i => i.PhysicalIndex).ToHashSet();

            for (int i = 0; i < buttonCount; i++)
            {
                if (covered.Contains(i)) continue;
                if (i + 1 > 32) break; // only 32 "[Wheel] Button N" slots exist

                filtered.Add(new PresetBindingItem
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

    /// <summary>
    /// Builds the Auto-Bind Wizard's (and the per-device "Output As" switch's) full preset list
    /// for one device: every axis/button this category is known to have that the device actually
    /// reports (see <see cref="FinalizePreset"/>), each pre-wired to a sensible default target in
    /// the chosen <paramref name="outputCategory"/>.
    /// </summary>
    public static List<PresetBindingItem> GeneratePreset(string deviceName, int buttonCount, int axisCount, OutputCategory outputCategory, bool hasPov = true, (int VendorId, int ProductId)? vidPid = null)
    {
        var list = new List<PresetBindingItem>();
        var (category, _) = ResolveCategoryAndProfile(deviceName, vidPid);

        if (category == DeviceHardwareCategory.PlayStationController)
        {
            // DirectInput Axes for DualSense (PS5) & DualShock 4 (PS4)
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick Horizontal", Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Left Stick X (Steer / Horizontal)", "[Wheel] Steering (Axis X)") });
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick Vertical", Type = InputType.Axis, PhysicalIndex = 1, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Left Stick Y (Steer / Vertical)", "[Wheel] Gas / Throttle (Axis Y)") });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick Horizontal", Type = InputType.Axis, PhysicalIndex = 2, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Right Stick X (Camera / Look Horizontal)", "[Wheel] Handbrake (Axis Ry)") });
            list.Add(new PresetBindingItem { PhysicalName = "L2 Trigger Axis", Type = InputType.Axis, PhysicalIndex = 3, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Left Trigger (LT / Brake Axis)", "[Wheel] Brake (Axis Z)") });
            list.Add(new PresetBindingItem { PhysicalName = "R2 Trigger Axis", Type = InputType.Axis, PhysicalIndex = 4, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Right Trigger (RT / Gas Axis)", "[Wheel] Gas / Throttle (Axis Y)") });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick Vertical", Type = InputType.Axis, PhysicalIndex = 5, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Right Stick Y (Camera / Look Vertical)", "[Wheel] Combined Slider 0") });

            // DirectInput Face Buttons & Controls
            list.Add(new PresetBindingItem { PhysicalName = "Cross (✕)", Type = InputType.Button, PhysicalIndex = 1, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox A (Cross / South / Handbrake)", "[Wheel] Button 1") });
            list.Add(new PresetBindingItem { PhysicalName = "Circle (○)", Type = InputType.Button, PhysicalIndex = 2, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox B (Circle / East)", "[Wheel] Button 2") });
            list.Add(new PresetBindingItem { PhysicalName = "Square (□)", Type = InputType.Button, PhysicalIndex = 0, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox X (Square / West / Shift Down)", "[Wheel] Button 3") });
            list.Add(new PresetBindingItem { PhysicalName = "Triangle (△)", Type = InputType.Button, PhysicalIndex = 3, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox Y (Triangle / North / Shift Up)", "[Wheel] Button 4") });
            list.Add(new PresetBindingItem { PhysicalName = "L1 Bumper", Type = InputType.Button, PhysicalIndex = 4, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)", "[Wheel] Paddle Down (Left Shift)") });
            list.Add(new PresetBindingItem { PhysicalName = "R1 Bumper", Type = InputType.Button, PhysicalIndex = 5, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox RB (Right Bumper / R1)", "[Wheel] Paddle Up (Right Shift)") });
            list.Add(new PresetBindingItem { PhysicalName = "Create / Share", Type = InputType.Button, PhysicalIndex = 8, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox View (Back / Map / Share)", "[Wheel] Button 11") });
            list.Add(new PresetBindingItem { PhysicalName = "Options / Menu", Type = InputType.Button, PhysicalIndex = 9, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox Menu (Start / Options)", "[Wheel] Button 12") });
            list.Add(new PresetBindingItem { PhysicalName = "L3 Stick Click", Type = InputType.Button, PhysicalIndex = 10, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox LSB (Left Stick Click / L3)", "[Wheel] Button 9") });
            list.Add(new PresetBindingItem { PhysicalName = "R3 Stick Click", Type = InputType.Button, PhysicalIndex = 11, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox RSB (Right Stick Click / R3)", "[Wheel] Button 10") });
            list.Add(new PresetBindingItem { PhysicalName = "PS Guide Button", Type = InputType.Button, PhysicalIndex = 12, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox Guide (Home / Guide)", "[Wheel] Button 13") });

            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Up", Type = InputType.Button, PhysicalIndex = 128, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] D-Pad Up", "[Wheel] Button 14") });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Down", Type = InputType.Button, PhysicalIndex = 130, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] D-Pad Down", "[Wheel] Button 15") });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Left", Type = InputType.Button, PhysicalIndex = 131, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] D-Pad Left", "[Wheel] Button 16") });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Right", Type = InputType.Button, PhysicalIndex = 129, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] D-Pad Right", "[Wheel] Button 17") });
            return FinalizePreset(list, deviceName, buttonCount, axisCount, hasPov, outputCategory, vidPid);
        }

        if (IsWheelOrPedalCategory(category))
        {
            bool wheelMode = outputCategory == OutputCategory.Wheel;

            // Only three physical axes have a brand-independent, verified identity: the wheel
            // rotation potentiometer (DirectInput always reports this as axis X for a device it
            // classifies as a wheel) and the first two pedal axes. Which further axis - if any -
            // is clutch vs. handbrake vs. a third pedal depends entirely on the attached pedal
            // set and isn't guessable from the index alone, so those axes (and every button) get
            // a default target only in Wheel mode, where the target is just the physical index
            // itself (see FinalizePreset's identity gap-filler below) rather than a functional
            // guess. Swapping a wheel rim or pedal set no longer breaks this preset, because it
            // no longer assumes anything about what's plugged into indices beyond these three.
            list.Add(new PresetBindingItem { PhysicalName = GetAxisDisplayName(deviceName, 0, vidPid), Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Left Stick X (Steer / Horizontal)", "[Wheel] Steering (Axis X)") });
            list.Add(new PresetBindingItem { PhysicalName = GetAxisDisplayName(deviceName, 1, vidPid), Type = InputType.Axis, PhysicalIndex = 1, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Right Trigger (RT / Gas Axis)", "[Wheel] Gas / Throttle (Axis Y)") });
            list.Add(new PresetBindingItem { PhysicalName = GetAxisDisplayName(deviceName, 2, vidPid), Type = InputType.Axis, PhysicalIndex = 2, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Left Trigger (LT / Brake Axis)", "[Wheel] Brake (Axis Z)") });

            if (wheelMode)
            {
                list.Add(new PresetBindingItem { PhysicalName = GetAxisDisplayName(deviceName, 3, vidPid), Type = InputType.Axis, PhysicalIndex = 3, DefaultTargetOutput = "[Wheel] Clutch (Axis Rx)" });
                list.Add(new PresetBindingItem { PhysicalName = GetAxisDisplayName(deviceName, 4, vidPid), Type = InputType.Axis, PhysicalIndex = 4, DefaultTargetOutput = "[Wheel] Handbrake (Axis Ry)" });

                // Every ordinary button (0..buttonCount-1) gets its "[Wheel] Button N" identity
                // binding from FinalizePreset's own gap-filler below - nothing to add here since
                // there's no per-category button table left to special-case around. D-Pad
                // continues the same sequential numbering right after the last regular button,
                // computed from the device's own reported buttonCount rather than a hardcoded
                // guess.
                list.Add(new PresetBindingItem { PhysicalName = GetButtonDisplayName(deviceName, 128, vidPid), Type = InputType.Button, PhysicalIndex = 128, DefaultTargetOutput = $"[Wheel] Button {buttonCount + 1}" });
                list.Add(new PresetBindingItem { PhysicalName = GetButtonDisplayName(deviceName, 130, vidPid), Type = InputType.Button, PhysicalIndex = 130, DefaultTargetOutput = $"[Wheel] Button {buttonCount + 2}" });
                list.Add(new PresetBindingItem { PhysicalName = GetButtonDisplayName(deviceName, 131, vidPid), Type = InputType.Button, PhysicalIndex = 131, DefaultTargetOutput = $"[Wheel] Button {buttonCount + 3}" });
                list.Add(new PresetBindingItem { PhysicalName = GetButtonDisplayName(deviceName, 129, vidPid), Type = InputType.Button, PhysicalIndex = 129, DefaultTargetOutput = $"[Wheel] Button {buttonCount + 4}" });
            }
            // Xbox/DS4 mode: every button, the clutch/handbrake axes, and the D-Pad are left
            // completely unassigned rather than guessed onto a face button/bumper/D-Pad slot -
            // the user maps them explicitly via the Rebinding Table once Identify Input (pressing
            // the physical control and watching which row lights up) tells them which raw index
            // it actually is.
        }
        else
        {
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick X", Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Left Stick X (Steer / Horizontal)", "[Wheel] Steering (Axis X)") });
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick Y", Type = InputType.Axis, PhysicalIndex = 1, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Left Stick Y (Steer / Vertical)", "[Wheel] Gas / Throttle (Axis Y)") });
            list.Add(new PresetBindingItem { PhysicalName = "Left Trigger (LT)", Type = InputType.Axis, PhysicalIndex = 2, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Left Trigger (LT / Brake Axis)", "[Wheel] Brake (Axis Z)") });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick X", Type = InputType.Axis, PhysicalIndex = 3, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Right Stick X (Camera / Look Horizontal)", "[Wheel] Handbrake (Axis Ry)") });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick Y", Type = InputType.Axis, PhysicalIndex = 4, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Right Stick Y (Camera / Look Vertical)", "[Wheel] Combined Slider 0") });
            list.Add(new PresetBindingItem { PhysicalName = "Right Trigger (RT)", Type = InputType.Axis, PhysicalIndex = 5, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Right Trigger (RT / Gas Axis)", "[Wheel] Gas / Throttle (Axis Y)") });

            list.Add(new PresetBindingItem { PhysicalName = "A Button", Type = InputType.Button, PhysicalIndex = 0, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox A (Cross / South / Handbrake)", "[Wheel] Button 1") });
            list.Add(new PresetBindingItem { PhysicalName = "B Button", Type = InputType.Button, PhysicalIndex = 1, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox B (Circle / East)", "[Wheel] Button 2") });
            list.Add(new PresetBindingItem { PhysicalName = "X Button", Type = InputType.Button, PhysicalIndex = 2, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox X (Square / West / Shift Down)", "[Wheel] Button 3") });
            list.Add(new PresetBindingItem { PhysicalName = "Y Button", Type = InputType.Button, PhysicalIndex = 3, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox Y (Triangle / North / Shift Up)", "[Wheel] Button 4") });
            list.Add(new PresetBindingItem { PhysicalName = "LB Bumper", Type = InputType.Button, PhysicalIndex = 4, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)", "[Wheel] Paddle Down (Left Shift)") });
            list.Add(new PresetBindingItem { PhysicalName = "RB Bumper", Type = InputType.Button, PhysicalIndex = 5, DefaultTargetOutput = ForCategory(outputCategory, "[Xbox] Xbox RB (Right Bumper / R1)", "[Wheel] Paddle Up (Right Shift)") });
        }

        return FinalizePreset(list, deviceName, buttonCount, axisCount, hasPov, outputCategory, vidPid);
    }

    /// <summary>
    /// Applies a generated preset to the shared mapping list for one device: for each preset
    /// item, if a <see cref="MappingEntry"/> already exists at that physical (<see cref="MappingEntry.SourceType"/>,
    /// <see cref="MappingEntry.SourceIndex"/>) slot, only its <see cref="MappingEntry.TargetOutput"/>/
    /// <see cref="MappingEntry.TargetDeviceId"/> are updated - its deadzone, inversion,
    /// calibration and hotspot link are left exactly as the user set them. Only slots with no
    /// existing entry get a brand-new one. Existing slots the new preset doesn't cover are left
    /// alone rather than removed. This is deliberately non-destructive (see the doc comment on
    /// <c>MainWindowViewModel.ApplyOutputModeToDevice</c> and the Auto-Bind Wizard's own
    /// instructions) - a prior version of this method deleted every existing entry for the
    /// device and rebuilt from scratch, silently wiping any customization on every re-run. Pure
    /// collection mutation with no ViewModel coupling, shared by
    /// <c>AutoBindWizardViewModel.ApplyWizardBindings</c> (wizard page) and
    /// <c>MainWindowViewModel.ApplyOutputModeToDevice</c> (the main grid's per-device "Output As"
    /// switch) so both entry points stay in sync instead of maintaining two copies of this logic.
    /// </summary>
    public static void ApplyPreset(ObservableCollection<MappingEntry> mappings, string deviceName, Guid deviceGuid, List<PresetBindingItem> presetItems, uint targetDeviceId)
    {
        var existingBySlot = mappings
            .Where(m => m.SourceDeviceName == deviceName)
            .GroupBy(m => (m.SourceType, m.SourceIndex))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var item in presetItems)
        {
            if (existingBySlot.TryGetValue((item.Type, item.PhysicalIndex), out var existing))
            {
                existing.TargetOutput = item.DefaultTargetOutput;
                existing.TargetDeviceId = targetDeviceId;
                continue;
            }

            // Keyed off the target channel rather than PhysicalName - the latter is now a
            // generic raw descriptor ("Axis Y") for wheel/rig categories and no longer carries
            // any functional wording to pattern-match against.
            bool shouldInvert = item.Type == InputType.Axis &&
                (item.DefaultTargetOutput.Contains("Vertical") || item.DefaultTargetOutput.Contains("Stick Y") ||
                 item.DefaultTargetOutput.Contains("Throttle") || item.DefaultTargetOutput.Contains("Gas") ||
                 item.DefaultTargetOutput.Contains("Brake"));

            mappings.Add(new MappingEntry
            {
                SourceDeviceName = deviceName,
                SourceDeviceGuid = deviceGuid,
                SourceType = item.Type,
                SourceIndex = item.PhysicalIndex,
                SourceDisplayName = item.PhysicalName,
                TargetDeviceId = targetDeviceId,
                TargetOutput = item.DefaultTargetOutput,
                Deadzone = item.Type == InputType.Axis ? 0.08 : 0.0,
                RawMin = 0,
                RawMax = 65535,
                IsInverted = shouldInvert
            });
        }
    }
}
