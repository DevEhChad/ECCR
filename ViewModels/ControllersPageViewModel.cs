using System.Collections.ObjectModel;
using ECCR.Models;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Controllers page: a richer per-device browser (name, connection type, battery)
/// than the plain name list shown elsewhere. Reuses
/// <see cref="DeviceStatusBarViewModel.GamepadStatuses"/> directly (the same shared instance
/// the top status bar already maintains) rather than rebuilding the same DirectInput+HID
/// correlation logic a second time.
/// </summary>
public partial class ControllersPageViewModel : ViewModelBase
{
    public ObservableCollection<ControllerStatus> Devices { get; }

    public bool HasDevices => Devices.Count > 0;

    /// <summary>Design-time constructor.</summary>
    public ControllersPageViewModel() : this(new ObservableCollection<ControllerStatus>())
    {
    }

    public ControllersPageViewModel(ObservableCollection<ControllerStatus> devices)
    {
        Devices = devices;
        Devices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDevices));
    }
}
