using System.Collections.Generic;

namespace ECCR.Models;

public class AppSettings
{
    public string LastActiveProfile { get; set; } = "Default";
    public string LastRunVersion { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; } = false;
    public bool RunInSystemTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseMinimizesToTray { get; set; } = true;
    public bool AutoCheckForUpdates { get; set; } = true;
    public bool IsHidHideActive { get; set; } = false;
    public bool IsAppListInverted { get; set; } = false;
    public List<string> BlockedInstanceIds { get; set; } = new();
}