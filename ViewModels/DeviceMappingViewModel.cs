using System.Collections.ObjectModel;
using ECCR.Models;

namespace ECCR.ViewModels;

/// <summary>
/// Thin forwarder backing the Device Mapping (Direct Input/Wheels) page: exposes
/// <see cref="MainWindowViewModel.WheelGroupedMappings"/> (the same shared instance, not a
/// copy). See <see cref="ControllerMappingViewModel"/> for the identical rationale - this is
/// the wheel/pedal-class counterpart of that page.
/// </summary>
public partial class DeviceMappingViewModel : ViewModelBase
{
    public ObservableCollection<DeviceMappingGroup> GroupEntries { get; }

    /// <summary>Design-time constructor.</summary>
    public DeviceMappingViewModel() : this(new ObservableCollection<DeviceMappingGroup>())
    {
    }

    public DeviceMappingViewModel(ObservableCollection<DeviceMappingGroup> groupEntries)
    {
        GroupEntries = groupEntries;
    }
}
