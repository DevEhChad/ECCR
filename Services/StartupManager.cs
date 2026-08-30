using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ECCR.Services;

/// <summary>
/// "Run on Windows Startup" via the classic per-user Run registry key (no scheduled task,
/// no startup folder shortcut). The registered command line always includes
/// <c>--minimized</c> so a login-triggered launch goes straight to the tray instead of
/// popping the main window open at boot - see <see cref="ECCR.App"/>'s handling of that flag.
/// </summary>
public static class StartupManager
{
    private const string AppName = "EhChads Controller Remapper";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\" --minimized");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch { }
    }
}