using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the persistent top device-status bar (visible on every page, not just Home - see
/// shell layout in <c>MainWindow.axaml</c>), which renders <see cref="AllDeviceStatuses"/> -
/// every connected physical device, gamepad or wheel/pedal (ECCR's own virtual ViGEm/vJoy
/// outputs are excluded upstream by <c>InputDeviceManager.GetConnectedDevices</c>, never
/// enumerated here at all). <see cref="GamepadStatuses"/> is the narrower gamepad-only subset
/// used by features that only make sense for gamepads (LED control, gyro/touch-aim source
/// pickers, the Home page's quick glance) - gamepad-class entries in <see cref="AllDeviceStatuses"/>
/// are the exact same instances held in <see cref="GamepadStatuses"/> (not copies), so
/// <see cref="ApplyHidStatuses"/>' in-place field mutations are automatically visible in both
/// collections.
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

    // GamepadStatuses is only ever added to/removed from on the UI thread (SyncConnectedDevices),
    // so MainWindowViewModel.ApplyMotionAndTouchAim - which needs to resolve a device name to its
    // VendorId/ProductId from ControllerHidService's own background poll thread - can't safely
    // enumerate it directly (ObservableCollection isn't thread-safe against concurrent Add/Remove).
    // This lock-guarded lookup is rebuilt alongside GamepadStatuses and is safe to query from any
    // thread; VendorId/ProductId never change after a ControllerStatus is created, only its
    // battery/gyro/touch fields do, so this snapshot never goes stale for the purpose it serves.
    private readonly object _vidPidLookupLock = new();
    private readonly Dictionary<string, (int VendorId, int ProductId)> _vidPidByDeviceName = new();

    public bool TryGetGamepadVidPid(string deviceInstanceName, out int vendorId, out int productId)
    {
        lock (_vidPidLookupLock)
        {
            if (_vidPidByDeviceName.TryGetValue(deviceInstanceName, out var vidPid))
            {
                (vendorId, productId) = vidPid;
                return true;
            }
        }

        vendorId = 0;
        productId = 0;
        return false;
    }

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

        lock (_vidPidLookupLock)
        {
            _vidPidByDeviceName.Clear();
            foreach (var s in GamepadStatuses)
            {
                _vidPidByDeviceName[s.DeviceInstanceName] = (s.VendorId, s.ProductId);
            }
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

    /// <summary>
    /// Fills connection type and battery for every non-Sony gamepad that Windows exposes through
    /// XInput (Xbox pads, wired or Bluetooth) - the counterpart to <see cref="ApplyHidStatuses"/>,
    /// which only covers Sony pads. Must run on the UI thread, like <see cref="ApplyHidStatuses"/>.
    /// </summary>
    public void ApplyXInputStatuses()
    {
        foreach (var status in GamepadStatuses)
        {
            if (status.VendorId == ControllerHidService.SonyVendorId) continue;
            if (!XInputStatusService.TryGetStatus(status.VendorId, status.ProductId, out var xinput)) continue;

            status.ConnectionType = xinput.Connection;
            status.IsBatterySupported = xinput.BatteryPercent.HasValue;
            status.BatteryPercent = xinput.BatteryPercent;
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
