namespace ECCR.Models;

/// <summary>
/// One row in the Auto-Map Wizard's preview grid, produced by
/// <see cref="ECCR.Services.DevicePresetService.GenerateAutoBindPreset"/>. A plain data holder
/// (deliberately not a service/ObservableObject) so the wizard's items collection binds
/// cleanly without pulling MVVM source-generator plumbing into a generated preset list.
/// Applying the wizard turns each of these into a full <see cref="MappingEntry"/>.
/// </summary>
public class PresetMappingItem
{
    public string PhysicalName { get; set; } = string.Empty;
    public InputType Type { get; set; } = InputType.Button;
    public int PhysicalIndex { get; set; }
    public string DefaultTargetOutput { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether the created mapping should start inverted (axes only).</summary>
    public bool IsInverted { get; set; }

    /// <summary>Raw window the axis is rescaled from. A combined trigger axis (XInput pads report LT and RT on one axis) is split into two items with half-range windows.</summary>
    public int RawMin { get; set; } = 0;
    public int RawMax { get; set; } = 65535;
}
