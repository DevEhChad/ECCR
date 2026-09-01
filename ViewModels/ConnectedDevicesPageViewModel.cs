using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Connected Devices page: every physically-connected gamepad or wheel/pedal device
/// in one list (virtual devices are already excluded upstream by
/// <see cref="ECCR.Services.InputDeviceManager"/>). Reuses
/// <see cref="DeviceStatusBarViewModel.AllDeviceStatuses"/> directly (the same shared instance,
/// not a copy) rather than rebuilding the same DirectInput+HID correlation logic a second time.
/// </summary>
public partial class ConnectedDevicesPageViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;

    public ObservableCollection<ControllerStatus> Devices { get; }

    public bool HasDevices => Devices.Count > 0;

    /// <summary>Routes a clicked device row to its Controller Mapping or Device Mapping page, pre-selected.</summary>
    public IRelayCommand<string> NavigateToDeviceMappingCommand => _host.NavigateToDeviceMappingCommand;

    /// <summary>Design-time constructor.</summary>
    public ConnectedDevicesPageViewModel() : this(new MainWindowViewModel(), new ObservableCollection<ControllerStatus>())
    {
    }

    public ConnectedDevicesPageViewModel(MainWindowViewModel host, ObservableCollection<ControllerStatus> devices)
    {
        _host = host;
        Devices = devices;
        Devices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDevices));
    }
}
