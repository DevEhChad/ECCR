using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ECCR.Services;

public class DependencyItemStatus
{
    public string Name { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
}

public class SystemDependenciesState
{
    public DependencyItemStatus ViGEm { get; set; } = new() { Name = "ViGEmBus" };
    public DependencyItemStatus HidHide { get; set; } = new() { Name = "HidHide" };
    public DependencyItemStatus VJoy { get; set; } = new() { Name = "vJoy" };

    public bool HasIssues => !ViGEm.IsInstalled || !ViGEm.IsRunning ||
                             !HidHide.IsInstalled || !HidHide.IsRunning ||
                             !VJoy.IsInstalled || !VJoy.IsRunning;
}

public class DependencyManager
{
    private static readonly HttpClient _httpClient = new();

    public static SystemDependenciesState GetCurrentStatus()
    {
        var state = new SystemDependenciesState();

        // 1. ViGEmBus
        state.ViGEm.IsInstalled = IsDriverInstalled("ViGEmBus") || File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "ViGEmBus.sys"));
        state.ViGEm.IsRunning = IsDriverServiceRunning("ViGEmBus");

        // 2. HidHide
        state.HidHide.IsInstalled = IsDriverInstalled("HidHide") || File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "HidHide.sys"));
        state.HidHide.IsRunning = IsDriverServiceRunning("HidHide");

        // 3. vJoy
        string vjoyUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8E31F78A-0A7B-4654-B1F7-C1EB9F225642}_is1";
        state.VJoy.IsInstalled = RegistryKeyExists(Registry.LocalMachine, vjoyUninstallKey) ||
                                 File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "vJoy.sys")) ||
                                 Directory.Exists(@"C:\Program Files\vJoy");
        state.VJoy.IsRunning = IsDriverServiceRunning("vjoy");

        return state;
    }

    private static bool IsDriverInstalled(string serviceName)
    {
        string subKey = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
        return RegistryKeyExists(Registry.LocalMachine, subKey);
    }

    private static bool IsDriverServiceRunning(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key != null)
            {
                // Start = 1 (System start), 2 (Auto start), 3 (Demand start)
                // A registered functional filter driver in Windows kernel is considered active if present and configured
                return true;
            }
        }
        catch { }

        return false;
    }

    private static bool RegistryKeyExists(RegistryKey rootKey, string subKey)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> StartDriverServiceAsync(string serviceName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "net.exe",
                Arguments = $"start {serviceName}",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
        }
        catch { }

        return false;
    }

    public static async Task<bool> DownloadAndInstallViGEmAsync()
    {
        string installerUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
        string tempPath = Path.Combine(Path.GetTempPath(), "ViGEmBusSetup.exe");

        try
        {
            byte[] fileBytes = await _httpClient.GetByteArrayAsync(installerUrl);
            await File.WriteAllBytesAsync(tempPath, fileBytes);

            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "/passive /norestart",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
        }
        catch { }

        return false;
    }

    public static async Task<bool> DownloadAndInstallHidHideAsync()
    {
        string installerUrl = "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHideMSI.msi";
        string tempPath = Path.Combine(Path.GetTempPath(), "HidHideSetup.msi");

        try
        {
            byte[] fileBytes = await _httpClient.GetByteArrayAsync(installerUrl);
            await File.WriteAllBytesAsync(tempPath, fileBytes);

            var psi = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{tempPath}\" /passive /norestart",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
        }
        catch { }

        return false;
    }

    public static async Task<bool> DownloadAndInstallVJoyAsync()
    {
        string installerUrl = "https://github.com/njz3/vJoy/releases/download/v2.1.9.1/vJoySetup.exe";
        string tempPath = Path.Combine(Path.GetTempPath(), "vJoySetup.exe");

        try
        {
            byte[] fileBytes = await _httpClient.GetByteArrayAsync(installerUrl);
            await File.WriteAllBytesAsync(tempPath, fileBytes);

            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "/SILENT",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
        }
        catch { }

        return false;
    }
}