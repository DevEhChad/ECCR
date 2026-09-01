using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the persistent top device-status bar (visible on every page, not just Home - see
/// shell layout in <c>MainWindow.axaml</c>). Holds one <see cref="ControllerStatus"/> per
/// connected gamepad-class device (wheels are deliberately excluded, per product decision).
/// Also holds <see cref="AllDeviceStatuses"/> - every physical device, gamepad or wheel/pedal -
/// for the Connected Devices page; gamepad-class entries there are the exact same instances
/// held in <see cref="GamepadStatuses"/> (not copies), so <see cref="ApplyHidStatuses"/>'
/// in-place field mutations are automatically visible in both collections.
/// Fed two ways by <see cref="MainWindowViewModel"/>: <see cref="SyncConnectedDevices"/> on
/// every DirectInput device-list refresh (name/connect-disconnect), and
/// <see cref="ApplyHidStatuses"/> on every <see cref="ControllerHidService"/> poll tick
/// (battery/charging/connection-type for Sony devices only).
/// </summary>
public partial class DeviceStatusBarViewModel : ViewModelBase
{
    public ObservableCollection<ControllerStatus> GamepadStatuses { get; } = new();
    public ObservableCollection<ControllerStatus> AllDeviceStatuses { get; } = new();

    public bool HasGamepads => GamepadStatuses.Count > 0;
    public bool HasDevices => AllDeviceStatuses.Count > 0;

    public DeviceStatusBarViewModel()
    {
        GamepadStatuses.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGamepads));
        AllDeviceStatuses.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDevices));
    }

    public void SyncConnectedDevices(List<DeviceHardwareInfo> devices)
    {
        var gamepadDevices = devices
            .Where(d => !DevicePresetService.IsWheelOrPedalCategory(DevicePresetService.DetectCategory(d.InstanceName)))
            .ToList();

        for (int i = GamepadStatuses.Count - 1; i >= 0; i--)
        {
            if (!gamepadDevices.Any(d => d.InstanceName == GamepadStatuses[i].DeviceInstanceName))
            {
                GamepadStatuses.RemoveAt(i);
            }
        }

        foreach (var d in gamepadDevices)
        {
            if (GamepadStatuses.Any(s => s.DeviceInstanceName == d.InstanceName)) continue;

            var (vendorId, productId) = ControllerHidService.GetVidPid(d.ProductGuid);
            GamepadStatuses.Add(new ControllerStatus
            {
                DeviceInstanceName = d.InstanceName,
                VendorId = vendorId,
                ProductId = productId,
                Category = DevicePresetService.DetectCategory(d.InstanceName)
            });
        }

        // AllDeviceStatuses: every physical device (gamepad + wheel/pedal), diffed the same
        // remove-then-add way as GamepadStatuses above - never a Clear()+rebuild, which would
        // momentarily empty the list and could snap a bound selection elsewhere back to
        // whatever ends up first once repopulated. Runs after the block above so a gamepad
        // device's ControllerStatus already exists in GamepadStatuses to reuse by reference.
        for (int i = AllDeviceStatuses.Count - 1; i >= 0; i--)
        {
            if (!devices.Any(d => d.InstanceName == AllDeviceStatuses[i].DeviceInstanceName))
            {
                AllDeviceStatuses.RemoveAt(i);
            }
        }

        foreach (var d in devices)
        {
            if (AllDeviceStatuses.Any(s => s.DeviceInstanceName == d.InstanceName)) continue;

            var category = DevicePresetService.DetectCategory(d.InstanceName);
            if (DevicePresetService.IsWheelOrPedalCategory(category))
            {
                // Wheels/pedals have no HID battery reporting - a bare name+category entry is enough.
                AllDeviceStatuses.Add(new ControllerStatus
                {
                    DeviceInstanceName = d.InstanceName,
                    Category = category
                });
            }
            else
            {
                AllDeviceStatuses.Add(GamepadStatuses.First(s => s.DeviceInstanceName == d.InstanceName));
            }
        }
    }

    public void ApplyHidStatuses(List<ControllerStatus> hidStatuses)
    {
        foreach (var hid in hidStatuses)
        {
            var match = GamepadStatuses.FirstOrDefault(s => s.VendorId == hid.VendorId && s.ProductId == hid.ProductId);
            if (match == null) continue;

            match.IsBatterySupported = hid.IsBatterySupported;
            match.BatteryPercent = hid.BatteryPercent;
            match.IsCharging = hid.IsCharging;
            match.ConnectionType = hid.ConnectionType;
            match.IsLedSupported = hid.IsLedSupported;
            match.IsMotionSupported = hid.IsMotionSupported;
            match.GyroPitch = hid.GyroPitch;
            match.GyroYaw = hid.GyroYaw;
            match.GyroRoll = hid.GyroRoll;
            match.TouchActive = hid.TouchActive;
            match.TouchX = hid.TouchX;
            match.TouchY = hid.TouchY;
        }
    }
}
