namespace ECCR.Models;

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