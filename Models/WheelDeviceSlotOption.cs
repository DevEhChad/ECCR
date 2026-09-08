namespace ECCR.Models;

/// <summary>
/// One entry in the "Assign to Wheel Slot" dropdown (<c>MainWindowViewModel.WheelSlots</c>,
/// a fixed list of 8). Same shape as <see cref="PlayerTargetOption"/> and bound the same way,
/// but deliberately a distinct type - a wheel slot and a Player are different numeric
/// namespaces sharing the same <see cref="MappingEntry.TargetDeviceId"/> field (vJoy device
/// rID for a <c>[Wheel]</c>-prefixed target vs. ViGEm/DS4 controller number for an
/// <c>[Xbox]</c>/<c>[DS4]</c>-prefixed one - see <see cref="MappingEntry.TargetDeviceId"/>'s
/// own doc comment). Having a separate option type (rather than reusing
/// <see cref="PlayerTargetOption"/> with a different Id range) keeps the two dropdowns visually
/// distinct at a glance and lets each carry its own color scheme.
/// </summary>
public class WheelDeviceSlotOption
{
    public uint Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ShortBadge { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TextColorHex { get; set; } = "#5CD8FF";
    public string BgColorHex { get; set; } = "#122730";
    public string BorderColorHex { get; set; } = "#00B8E5";
}
