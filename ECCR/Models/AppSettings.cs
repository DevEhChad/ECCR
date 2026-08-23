namespace ECCR.Models;

public class AppSettings
{
    public string LastActiveProfile { get; set; } = "Default";
    public bool StartWithWindows { get; set; } = false;
    public bool RunInSystemTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseMinimizesToTray { get; set; } = true;
    public bool AutoCheckForUpdates { get; set; } = true;
}