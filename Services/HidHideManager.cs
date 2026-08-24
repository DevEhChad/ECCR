using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nefarius.Drivers.HidHide;

namespace ECCR.Services;

public class HidDeviceItem
{
    public string DeviceInstanceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
}

public class HidHideManager
{
    private readonly IHidHideControlService? _hidHide;

    public bool IsInstalled => _hidHide?.IsInstalled ?? false;

    public bool IsGlobalActive
    {
        get
        {
            try { return _hidHide?.IsActive ?? false; }
            catch { return false; }
        }
    }

    public bool IsAppListInverted
    {
        get
        {
            try { return _hidHide?.IsAppListInverted ?? false; }
            catch { return false; }
        }
        set
        {
            try { if (_hidHide != null) _hidHide.IsAppListInverted = value; }
            catch { }
        }
    }

    public HidHideManager()
    {
        try
        {
            _hidHide = new HidHideControlService();
        }
        catch
        {
            _hidHide = null;
        }
    }

    public void SetGlobalHidingState(bool active)
    {
        try
        {
            if (_hidHide != null && _hidHide.IsInstalled)
            {
                _hidHide.IsActive = active;
            }
        }
        catch { }
    }

    public List<string> GetBlockedInstanceIds()
    {
        if (_hidHide == null || !_hidHide.IsInstalled) return new List<string>();
        try { return _hidHide.BlockedInstanceIds.ToList(); }
        catch { return new List<string>(); }
    }

    public List<HidDeviceItem> GetConnectedHidDevices()
    {
        var list = new List<HidDeviceItem>();
        if (_hidHide == null || !_hidHide.IsInstalled) return list;

        try
        {
            var blockedInstances = new HashSet<string>(_hidHide.BlockedInstanceIds, StringComparer.OrdinalIgnoreCase);
            var devManager = new InputDeviceManager();
            var connected = devManager.GetConnectedDevices();

            foreach (var d in connected)
            {
                string instanceId = $"HID\\{d.ProductGuid}";
                list.Add(new HidDeviceItem
                {
                    DeviceInstanceId = instanceId,
                    FriendlyName = d.InstanceName,
                    IsHidden = blockedInstances.Contains(instanceId)
                });
            }
        }
        catch { }

        return list;
    }

    public void ToggleDeviceHiding(HidDeviceItem device, bool hide)
    {
        if (_hidHide == null || !_hidHide.IsInstalled) return;

        try
        {
            if (hide)
            {
                _hidHide.AddBlockedInstanceId(device.DeviceInstanceId);
            }
            else
            {
                _hidHide.RemoveBlockedInstanceId(device.DeviceInstanceId);
            }
            device.IsHidden = hide;
        }
        catch { }
    }

    public void ApplyBlockedInstances(IEnumerable<string> instanceIds)
    {
        if (_hidHide == null || !_hidHide.IsInstalled) return;

        try
        {
            var current = new HashSet<string>(_hidHide.BlockedInstanceIds, StringComparer.OrdinalIgnoreCase);
            foreach (var id in instanceIds)
            {
                if (!current.Contains(id))
                {
                    _hidHide.AddBlockedInstanceId(id);
                }
            }
        }
        catch { }
    }

    public List<string> GetApplicationExemptions()
    {
        if (_hidHide == null || !_hidHide.IsInstalled) return new List<string>();
        try { return _hidHide.ApplicationPaths.ToList(); }
        catch { return new List<string>(); }
    }

    public void AddApplicationExemption(string fullExePath)
    {
        if (_hidHide == null || !_hidHide.IsInstalled || string.IsNullOrWhiteSpace(fullExePath)) return;
        try
        {
            _hidHide.AddApplicationPath(fullExePath);
        }
        catch { }
    }

    public void AddDirectoryExemptions(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;
        try
        {
            var exeFiles = Directory.GetFiles(directoryPath, "*.exe", SearchOption.AllDirectories);
            foreach (var exe in exeFiles)
            {
                AddApplicationExemption(exe);
            }
        }
        catch { }
    }

    public void RemoveApplicationExemption(string fullExePath)
    {
        if (_hidHide == null || !_hidHide.IsInstalled) return;
        try
        {
            _hidHide.RemoveApplicationPath(fullExePath);
        }
        catch { }
    }

    public void ClearAllApplicationExemptions()
    {
        if (_hidHide == null || !_hidHide.IsInstalled) return;
        try
        {
            foreach (var path in _hidHide.ApplicationPaths.ToList())
            {
                _hidHide.RemoveApplicationPath(path);
            }
        }
        catch { }
    }
}