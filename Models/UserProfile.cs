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
}