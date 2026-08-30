using System.Collections.ObjectModel;
using ECCR.Models;

namespace ECCR.ViewModels;

/// <summary>
/// Thin forwarder backing the Controller Mapping page: exposes
/// <see cref="MainWindowViewModel.GamepadGroupedMappings"/> (the same shared instance, not a
/// copy) so the page's XAML can bind an <c>ItemsSource</c> without owning any mapping logic
/// itself. Every command in the row templates still reaches back to
/// <see cref="MainWindowViewModel"/> directly via the "#RootWindow.((vm:MainWindowViewModel)
/// DataContext).X" compiled-binding path the original grid used, since that path still
/// resolves once this page is hosted inside <c>MainWindow</c>'s named root.
/// </summary>
public partial class ControllerMappingViewModel : ViewModelBase
{
    public ObservableCollection<DeviceMappingGroup> GroupEntries { get; }

    /// <summary>Design-time constructor.</summary>
    public ControllerMappingViewModel() : this(new ObservableCollection<DeviceMappingGroup>())
    {
    }

    public ControllerMappingViewModel(ObservableCollection<DeviceMappingGroup> groupEntries)
    {
        GroupEntries = groupEntries;
    }
}
