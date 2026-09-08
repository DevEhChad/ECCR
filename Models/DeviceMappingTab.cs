namespace ECCR.Models;

/// <summary>
/// Tabs on the unified Device Mappings page (see <see cref="ECCR.ViewModels.DeviceMappingPageViewModel"/>).
/// <see cref="Mappings"/> is always first and available for every device (the visual diagram +
/// Device Mapping Table, replacing the old per-page "Button Mapping" grid); <see cref="InputMatrix"/>
/// is likewise always available (a capability-driven button/POV tile grid that works for any
/// device regardless of rim/box layout - see <c>ECCR.ViewModels.InputMatrixViewModel</c>); the
/// rest are device-type-conditional, shown only for a gamepad (<see cref="Deadzones"/>/<see cref="Sticks"/>/
/// <see cref="Motion"/>/<see cref="Touchpad"/>/<see cref="MoreOptions"/> - ported from the old
/// Controller Mapping page's <c>MappingTab</c>) or only for a wheel/pedal device
/// (<see cref="WheelSettings"/> - ported from the old, now-consolidated Device Mapping page).
/// </summary>
public enum DeviceMappingTab
{
    Mappings,
    InputMatrix,
    Deadzones,
    Sticks,
    Triggers,
    Motion,
    Touchpad,
    WheelSettings,
    Calibration,
    PedalSettings,
    MoreOptions
}
