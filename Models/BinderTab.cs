namespace ECCR.Models;

/// <summary>
/// Tabs on the unified Binder page (see <see cref="ECCR.ViewModels.BinderPageViewModel"/>).
/// <see cref="Bindings"/> is always first and available for every device (the visual diagram +
/// Rebinding Table, replacing the old per-page "Button Mapping" grid); the rest are
/// device-type-conditional, shown only for a gamepad (<see cref="Deadzones"/>/<see cref="Sticks"/>/
/// <see cref="Motion"/>/<see cref="Touchpad"/>/<see cref="MoreOptions"/> - ported from the old
/// Controller Mapping page's <c>MappingTab</c>) or only for a wheel/pedal device
/// (<see cref="WheelSettings"/> - ported from the old Device Mapping page's <c>DeviceMappingTab</c>).
/// </summary>
public enum BinderTab
{
    Bindings,
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
