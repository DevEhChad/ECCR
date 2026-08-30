using System.Collections.ObjectModel;
using System.Linq;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Direct Input/Wheels page: a device browser like <see cref="ControllersPageViewModel"/>
/// but for wheel/pedal-class devices, filtered from the same
/// <see cref="MainWindowViewModel.ConnectedDevices"/> list. No battery/connection-type columns
/// here - that HID reporting only applies to Sony gamepads, not wheel hardware.
/// </summary>
public partial class WheelsPageViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;

    public ObservableCollection<string> Devices { get; } = new();

    public bool HasDevices => Devices.Count > 0;

    /// <summary>Design-time constructor.</summary>
    public WheelsPageViewModel() : this(new MainWindowViewModel())
    {
    }

    public WheelsPageViewModel(MainWindowViewModel host)
    {
        _host = host;
        Devices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDevices));
        _host.ConnectedDevices.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var wheelDevices = _host.ConnectedDevices
            .Where(name => DevicePresetService.IsWheelOrPedalCategory(DevicePresetService.DetectCategory(name)))
            .ToList();

        Devices.Clear();
        foreach (var name in wheelDevices) Devices.Add(name);
    }
}
