namespace ECCR.Models;

/// <summary>
/// One row in the Auto-Bind Wizard's preview grid, produced by
/// <see cref="ECCR.Services.DevicePresetService.GeneratePreset"/>. A plain data holder
/// (deliberately not a service/ObservableObject) so the wizard's items collection binds
/// cleanly without pulling MVVM source-generator plumbing into a generated preset list.
/// Applying the wizard turns each of these into a full <see cref="MappingEntry"/>.
/// </summary>
public class PresetBindingItem
{
    public string PhysicalName { get; set; } = string.Empty;
    public InputType Type { get; set; } = InputType.Button;
    public int PhysicalIndex { get; set; }
    public string DefaultTargetOutput { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}