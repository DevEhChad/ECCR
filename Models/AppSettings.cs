using System.Collections.Generic;

namespace ECCR.Models;

/// <summary>
/// Serialized as-is to <c>%AppData%/ECCR/settings.json</c> (see
/// <see cref="ECCR.ViewModels.MainWindowViewModel.SaveAppSettings"/>). Holds app-wide
/// preferences and HidHide state; per-mapping data (device bindings, calibration) lives in
/// separate profile files instead (<see cref="UserProfile"/>).
/// </summary>
public class AppSettings
{
    public string LastActiveProfile { get; set; } = "Default";
    public string LastRunVersion { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; } = false;
    public bool RunInSystemTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseMinimizesToTray { get; set; } = true;
    public bool AutoCheckForUpdates { get; set; } = true;
    public bool ShowChangelogOnUpdate { get; set; } = true;
    public bool IsVirtualOutputActive { get; set; } = true;
    public bool IsHidHideActive { get; set; } = false;
    public bool IsAppListInverted { get; set; } = false;
    public bool IsSidebarCollapsed { get; set; } = false;

    /// <summary>Filename stem (no extension) of the Visual Binder's controller skin, under Assets/ControllerSkins/.</summary>
    public string ControllerSkin { get; set; } = "White-Red-Faced";

    // Gyro-aim / touch-aim: map a Sony device's motion sensor or touchpad to the Right Stick
    // of a chosen Player's virtual Xbox 360 controller. App-wide (not per-profile) since which
    // physical device supplies motion/touch input is a hardware fact, not a per-game mapping.
    public bool GyroAimEnabled { get; set; } = false;
    public string? GyroAimSourceDeviceName { get; set; }
    public uint GyroAimTargetPlayer { get; set; } = 1;
    public bool TouchAimEnabled { get; set; } = false;
    public string? TouchAimSourceDeviceName { get; set; }
    public uint TouchAimTargetPlayer { get; set; } = 1;

    public List<string> BlockedInstanceIds { get; set; } = new();
    public List<string> WhitelistedApplications { get; set; } = new();
}