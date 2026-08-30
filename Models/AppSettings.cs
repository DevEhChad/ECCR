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
    public bool IsVirtualOutputActive { get; set; } = true;
    public bool IsHidHideActive { get; set; } = false;
    public bool IsAppListInverted { get; set; } = false;
    public List<string> BlockedInstanceIds { get; set; } = new();
    public List<string> WhitelistedApplications { get; set; } = new();
}