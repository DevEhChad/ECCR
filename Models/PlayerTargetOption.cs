namespace ECCR.Models;

/// <summary>
/// One entry in the "Assign to Player" dropdown (<c>MainWindowViewModel.PlayerTargets</c>,
/// a fixed list of 4). Purely a display/theming wrapper around a numeric target - <see cref="Id"/>
/// is what actually gets written to <see cref="MappingEntry.TargetDeviceId"/> and doubles as
/// the vJoy/ViGEm virtual device number that entry's output is routed to.
/// </summary>
public class PlayerTargetOption
{
    public uint Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ShortBadge { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TextColorHex { get; set; } = "#5CFFBE";
    public string BgColorHex { get; set; } = "#193627";
    public string BorderColorHex { get; set; } = "#00E599";
}