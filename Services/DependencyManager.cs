using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ECCR.Services;

public class DependencyStatus
{
    public (bool IsInstalled, bool IsRunning) ViGEm { get; set; }
    public (bool IsInstalled, bool IsRunning) HidHide { get; set; }
    public (bool IsInstalled, bool IsRunning) VJoy { get; set; }
    public bool HasIssues => !ViGEm.IsInstalled || !ViGEm.IsRunning ||
                             !HidHide.IsInstalled || !HidHide.IsRunning ||
                             !VJoy.IsInstalled || !VJoy.IsRunning;
}

public static class DependencyManager
{
    public static DependencyStatus GetCurrentStatus()
    {
        return new DependencyStatus
        {
            ViGEm = CheckDriverStatus("ViGEmBus", @"SYSTEM\CurrentControlSet\Services\ViGEmBus"),
            HidHide = CheckDriverStatus("HidHide", @"SYSTEM\CurrentControlSet\Services\HidHide"),
            VJoy = CheckVJoyStatus()
        };
    }

    private static (bool IsInstalled, bool IsRunning) CheckDriverStatus(string serviceName, string registrySubKey)
    {
        bool isInstalled = false;
        bool isRunning = false;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(registrySubKey);
            if (key != null) isInstalled = true;
        }
        catch { }

        try
        {
            using var sc = new ServiceController(serviceName);
            isInstalled = true;
            isRunning = (sc.Status == ServiceControllerStatus.Running);
        }
        catch
        {
            isRunning = false;
        }

        return (isInstalled, isRunning);
    }

    private static (bool IsInstalled, bool IsRunning) CheckVJoyStatus()
    {
        bool isInstalled = false;
        bool isRunning = false;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\vjoy");
            if (key != null) isInstalled = true;
        }
        catch { }

        try
        {
            using var sc = new ServiceController("vjoy");
            isInstalled = true;
            isRunning = (sc.Status == ServiceControllerStatus.Running);
        }
        catch
        {
            isRunning = false;
        }

        if (!isInstalled)
        {
            string systemVJoyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "vJoyInterface.dll");
            if (File.Exists(systemVJoyPath))
            {
                isInstalled = true;
                isRunning = true;
            }
        }

        return (isInstalled, isRunning);
    }

    public static async Task<bool> StartDriverServiceAsync(string serviceName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                }
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public static async Task DownloadAndInstallViGEmAsync()
    {
        string url = "https://github.com/nefarius/ViGEmBus/releases/latest/download/ViGEmBus_Setup_x64.exe";
        await DownloadAndExecuteInstallerAsync(url, "ViGEmBusSetup.exe", "/quiet");
    }

    public static async Task DownloadAndInstallHidHideAsync()
    {
        string url = "https://github.com/nefarius/HidHide/releases/latest/download/HidHideMSI.msi";
        await DownloadAndExecuteInstallerAsync(url, "HidHideSetup.msi", "/quiet /qn");
    }

    public static async Task DownloadAndInstallVJoyAsync()
    {
        string url = "https://github.com/njz3/vJoy/releases/download/v2.1.9.1/vJoySetup.exe";
        await DownloadAndExecuteInstallerAsync(url, "vJoySetup.exe", "/SILENT");
    }

    private static async Task DownloadAndExecuteInstallerAsync(string url, string tempFileName, string arguments)
    {
        try
        {
            string tempPath = Path.Combine(Path.GetTempPath(), tempFileName);
            using (var client = new HttpClient())
            {
                var bytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(tempPath, bytes);
            }

            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            };

            var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
            }
        }
        catch { }
    }
}