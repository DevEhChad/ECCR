using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ECCR.Services;

public enum DriverType
{
    ViGEmBus,
    HidHide,
    VJoy
}

public class DriverStatusInfo
{
    public bool IsInstalled { get; set; }
    public string Version { get; set; } = "Not Found";
    public string? UninstallString { get; set; }
}

/// <summary>
/// Detects and (un)installs the three native drivers ECCR depends on: ViGEmBus (virtual
/// Xbox/DualShock pads), HidHide (device cloaking), and vJoy (virtual DirectInput wheel).
/// Detection is registry-based rather than an API call (see <see cref="CheckServiceOrRegistry"/>)
/// since all three ship as either a kernel service, a listed "Programs and Features" entry,
/// or both. Installers are downloaded straight from each project's GitHub Releases and run
/// elevated (UAC prompt) - there's no bundled/offline installer.
/// </summary>
public class DependencyManager
{
    private static readonly HttpClient _httpClient = new();
    private static readonly string TempDriverFolder = Path.Combine(Path.GetTempPath(), "ECCR_Drivers");

    // Official direct download URLs
    private const string ViGEmBusUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
    private const string HidHideUrl = "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe";
    private const string VJoyUrl = "https://github.com/jshafer817/vJoy/releases/latest/download/vJoySetup.exe";

    public DriverStatusInfo CheckViGEmBus()
    {
        return CheckServiceOrRegistry("ViGEmBus", "Virtual Gamepad Emulation Bus");
    }

    public DriverStatusInfo CheckHidHide()
    {
        return CheckServiceOrRegistry("HidHide", "HidHide");
    }

    public DriverStatusInfo CheckVJoy()
    {
        return CheckServiceOrRegistry("vJoy", "vJoy");
    }

    public async Task<bool> InstallDriverAsync(DriverType driver, IProgress<string>? progress = null)
    {
        string url = driver switch
        {
            DriverType.ViGEmBus => ViGEmBusUrl,
            DriverType.HidHide => HidHideUrl,
            DriverType.VJoy => VJoyUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(driver))
        };

        string fileName = Path.GetFileName(new Uri(url).LocalPath);
        Directory.CreateDirectory(TempDriverFolder);
        string destinationPath = Path.Combine(TempDriverFolder, fileName);

        try
        {
            progress?.Report($"Downloading {driver}...");
            byte[] fileBytes = await _httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(destinationPath, fileBytes);

            progress?.Report($"Installing {driver} (Elevated)...");
            var psi = new ProcessStartInfo
            {
                FileName = destinationPath,
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DependencyManager] Installation failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UninstallDriverAsync(DriverType driver)
    {
        var status = driver switch
        {
            DriverType.ViGEmBus => CheckViGEmBus(),
            DriverType.HidHide => CheckHidHide(),
            DriverType.VJoy => CheckVJoy(),
            _ => null
        };

        if (status == null || string.IsNullOrWhiteSpace(status.UninstallString))
        {
            Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
            return true;
        }

        try
        {
            string cmd = status.UninstallString.Trim();
            string fileName;
            string args = "";

            if (cmd.StartsWith("\""))
            {
                int nextQuote = cmd.IndexOf('"', 1);
                fileName = cmd.Substring(1, nextQuote - 1);
                args = cmd.Substring(nextQuote + 1).Trim();
            }
            else
            {
                var parts = cmd.Split(' ', 2);
                fileName = parts[0];
                if (parts.Length > 1) args = parts[1];
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DependencyManager] Uninstall failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// A driver counts as "installed" if either its kernel service key exists under
    /// Services (fast check, doesn't need the service to be running) or an
    /// Uninstall registry entry matching <paramref name="displayNameSubstring"/> is found
    /// (also checked so an app that installed a driver's userspace tooling but not the
    /// kernel-mode service - or vice versa - still reports something sensible, and so the
    /// uninstall string is available for <see cref="UninstallDriverAsync"/>).
    /// </summary>
    private DriverStatusInfo CheckServiceOrRegistry(string serviceName, string displayNameSubstring)
    {
        var info = new DriverStatusInfo();

        using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}"))
        {
            if (key != null)
            {
                info.IsInstalled = true;
                info.Version = "Active (Service)";
            }
        }

        string[] uninstallKeys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var path in uninstallKeys)
        {
            using var root = Registry.LocalMachine.OpenSubKey(path);
            if (root == null) continue;

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                using var subKey = root.OpenSubKey(subKeyName);
                var name = subKey?.GetValue("DisplayName")?.ToString();
                if (name != null && name.Contains(displayNameSubstring, StringComparison.OrdinalIgnoreCase))
                {
                    info.IsInstalled = true;
                    info.Version = subKey?.GetValue("DisplayVersion")?.ToString() ?? "Installed";
                    info.UninstallString = subKey?.GetValue("UninstallString")?.ToString();
                    return info;
                }
            }
        }

        return info;
    }
}