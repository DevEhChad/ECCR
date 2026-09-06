using System;
using System.Collections.Generic;

namespace ECCR.Models;

/// <summary>
/// One saved binding set, serialized to <c>%AppData%/ECCR/Profiles/{ProfileName}.json</c>.
/// The user can keep several profiles (e.g. per game) and switch the active one from the
/// main window; only <see cref="Mappings"/> and <see cref="LedRed"/>/<see cref="LedGreen"/>/
/// <see cref="LedBlue"/> are actually read back on load - <see cref="OutputMode"/> is written
/// for forward-compatibility but not currently consulted (each <see cref="MappingEntry"/>
/// already carries its own target type via its <c>TargetOutput</c> string prefix).
/// </summary>
public class UserProfile
{
    /// <summary>
    /// Stable identity that survives a rename (unlike <see cref="ProfileName"/>, which is also
    /// the filename) - <see cref="GameInfo.BoundProfileId"/> keys off this. Deliberately has NO
    /// default-value initializer: a field initializer would run during deserialization's
    /// object-construction step and, since old files have no "Id" property to overwrite it, every
    /// old file would silently get a fresh random Id on every single load instead of ever being
    /// migrated once. Every code path that creates a genuinely new profile
    /// (<see cref="ECCR.Services.ProfileService"/>'s create/duplicate/bootstrap paths) sets this
    /// explicitly; a profile file saved before this field existed deserializes with
    /// <c>Id == Guid.Empty</c>, which <see cref="ECCR.Services.ProfileService"/> detects and
    /// migrates (assigns + rewrites the file) exactly once.
    /// </summary>
    public Guid Id { get; set; }

    public string ProfileName { get; set; } = "Default";
    public VirtualEmulationMode OutputMode { get; set; } = VirtualEmulationMode.DirectInputWheel;
    public List<MappingEntry> Mappings { get; set; } = [];

    /// <summary>Saved Controller LED lightbar color for this profile (defaults match <see cref="ECCR.ViewModels.ControllerLedViewModel"/>'s in-memory defaults).</summary>
    public byte LedRed { get; set; } = 0;
    public byte LedGreen { get; set; } = 122;
    public byte LedBlue { get; set; } = 255;

    /// <summary>Saved lightbar animation mode/speed for this profile - see <see cref="LedEffectMode"/>.</summary>
    public LedEffectMode LedMode { get; set; } = LedEffectMode.Static;
    public double LedSpeed { get; set; } = 1.0;

    /// <summary>Rotation/force-feedback tuning per wheel device name - see <see cref="WheelDeviceSettings"/>.</summary>
    public Dictionary<string, WheelDeviceSettings> WheelSettings { get; set; } = new();

    /// <summary>Rumble/LED tuning per gamepad device name - see <see cref="GamepadDeviceSettings"/>. Empty on profiles saved before this existed; <c>MainWindowViewModel</c> seeds it once from the legacy <see cref="LedRed"/>/<see cref="LedGreen"/>/<see cref="LedBlue"/>/<see cref="LedMode"/>/<see cref="LedSpeed"/> fields above on first load.</summary>
    public Dictionary<string, GamepadDeviceSettings> GamepadSettings { get; set; } = new();
}