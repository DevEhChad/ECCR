using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Services;

namespace ECCR.ViewModels;

public partial class HidHideViewModel : ViewModelBase
{
    private readonly HidHideManager _hidHideManager;

    [ObservableProperty]
    private bool _isFirewallActive;

    [ObservableProperty]
    private bool _isDriverInstalled;

    public ObservableCollection<HidDeviceItem> Devices { get; } = new();

    public HidHideViewModel()
    {
        _hidHideManager = new HidHideManager();
        IsDriverInstalled = _hidHideManager.IsInstalled;
        IsFirewallActive = _hidHideManager.IsGlobalActive;

        RefreshDevices();
    }

    public void RefreshDevices()
    {
        Devices.Clear();
        var items = _hidHideManager.GetConnectedHidDevices();
        foreach (var item in items)
        {
            Devices.Add(item);
        }
    }

    [RelayCommand]
    public void ToggleDevice(HidDeviceItem item)
    {
        _hidHideManager.ToggleDeviceHiding(item, item.IsHidden);
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var device in Devices)
        {
            if (!device.IsHidden)
            {
                device.IsHidden = true;
                _hidHideManager.ToggleDeviceHiding(device, true);
            }
        }
    }

    [RelayCommand]
    public void DeselectAll()
    {
        foreach (var device in Devices)
        {
            if (device.IsHidden)
            {
                device.IsHidden = false;
                _hidHideManager.ToggleDeviceHiding(device, false);
            }
        }
    }

    partial void OnIsFirewallActiveChanged(bool value)
    {
        _hidHideManager.SetGlobalHidingState(value);
    }
}