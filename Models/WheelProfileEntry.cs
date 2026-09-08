using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ECCR.Models;

/// <summary>
/// One entry in <c>wheel_profiles.json</c> (see <see cref="ECCR.Services.WheelProfileService"/>):
/// an optional override, keyed by USB VID/PID, that can correct or extend
/// <see cref="ECCR.Services.DevicePresetService"/>'s hardcoded per-brand naming tables without a
/// recompile. <see cref="ProductId"/> null means "match this vendor regardless of product" -
/// useful for a vendor whose whole lineup shares one button layout. Any index not present in
/// <see cref="Buttons"/>/<see cref="Axes"/> falls through to <see cref="Category"/>'s hardcoded
/// table (if set) or the generic "Wheel Button N" naming - this file only ever adds coverage, it
/// never has to be complete for a device to work.
/// </summary>
public class WheelProfileEntry
{
    public int VendorId { get; set; }
    public int? ProductId { get; set; }

    /// <summary>Optional display name for this specific wheel/rig, shown nowhere yet but reserved for future use (e.g. an "Auto-detected: {Name}" hint).</summary>
    public string? Name { get; set; }

    /// <summary>Overrides <see cref="ECCR.Services.DevicePresetService.DetectCategory"/>'s result when this entry matches - lets an unrecognized device still pick up an existing category's button/axis tables (e.g. a new Fanatec wheel base with a name DetectCategory doesn't recognize).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DeviceHardwareCategoryDto? Category { get; set; }

    /// <summary>Raw button/POV index (as string keys, since JSON object keys are always strings) -&gt; display label override.</summary>
    public Dictionary<string, string>? Buttons { get; set; }

    /// <summary>Raw axis index (0-7, fixed X/Y/Z/RotationX/RotationY/RotationZ/Slider0/Slider1 order) -&gt; display label override.</summary>
    public Dictionary<string, string>? Axes { get; set; }
}

/// <summary>
/// JSON-facing mirror of <see cref="ECCR.Services.DeviceHardwareCategory"/> - kept separate so
/// <c>wheel_profiles.json</c>'s <c>"category"</c> field has a stable string contract even if the
/// internal enum's members are ever reordered or renamed.
/// </summary>
public enum DeviceHardwareCategoryDto
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
