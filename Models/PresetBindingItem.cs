namespace ECCR.Models;

public class PresetBindingItem
{
    public string PhysicalName { get; set; } = string.Empty;
    public InputType Type { get; set; } = InputType.Button;
    public int PhysicalIndex { get; set; }
    public string DefaultTargetOutput { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
