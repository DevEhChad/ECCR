using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace ECCR.Services;

/// <summary>
/// Thin wrapper over Velopack's <see cref="UpdateManager"/>, pointed at the *public* release
/// repo (not the private dev mirror) - this is the "did the user get the new installer"
/// counterpart to the "did we ship one" side handled by build-release.ps1 (not checked into
/// this repo). <see cref="UpdateManager.IsInstalled"/> being false means the app is running
/// from a plain `dotnet run`/debug build rather than a Velopack-installed copy, in which
/// case update checks are silently skipped rather than erroring.
///
/// <para>
/// Velopack does the actual HTTP/JSON/semver/download/install work internally - this class
/// doesn't reimplement any of that (per the "audit, don't redesign" scope). What it does add is
/// classifying whatever Velopack throws into a specific, loggable reason instead of one bare
/// <c>catch</c>, so a report of "updates never work" is diagnosable from <c>update-debug.log</c>
/// after the fact even though a background check never bothers the user about it.
/// </para>
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "https://github.com/DevEhChad/ECCR";

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "update-debug.log");
    private const long MaxLogSizeBytes = 5 * 1024 * 1024;

    /// <summary>Short, specific reason the most recent check/download/apply failed for - null after a successful call. Never shown unprompted for a background check (see <c>MainWindowViewModel.CheckForUpdatesAsync</c>'s isBackgroundCheck guard); available for a manual check's status text either way.</summary>
    public string? LastError { get; private set; }

    private static void LogDebug(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogSizeBytes)
                File.Delete(LogFilePath);
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] [Update] {message}\n");
        }
        catch { }
    }

    /// <summary>
    /// Classifies whatever Velopack's HTTP/JSON layer threw into a specific message (timeout,
    /// no internet, GitHub rate limit, malformed response, locked/unwritable installer file)
    /// instead of one generic failure, and always logs the real type+message either way.
    /// </summary>
    private void HandleUpdateException(string context, Exception ex)
    {
        LastError = ex switch
        {
            OperationCanceledException => "Update check timed out.",
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests } =>
                "GitHub rate-limited the update check - try again later.",
            HttpRequestException => "Could not reach the update server - check your internet connection.",
            JsonException => "The update server returned a response ECCR couldn't understand.",
            UnauthorizedAccessException => "Update install was blocked - ECCR doesn't have permission to write to its install folder.",
            IOException => "Update install failed - the installer file may be locked by another process.",
            _ => $"Update {context} failed: {ex.Message}"
        };

        LogDebug($"{context} failed: {ex.GetType().Name}: {ex.Message}");
    }

    private UpdateManager CreateManager()
    {
        var source = new GithubSource(RepoUrl, string.Empty, false);
        return new UpdateManager(source);
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var mgr = CreateManager();
            if (!mgr.IsInstalled) return null;
            var info = await mgr.CheckForUpdatesAsync();
            LastError = null;
            return info;
        }
        catch (Exception ex)
        {
            HandleUpdateException("check", ex);
            return null;
        }
    }

    public async Task<bool> DownloadAndApplyAsync(UpdateInfo updateInfo, Action<int>? progressCallback = null)
    {
        try
        {
            var mgr = CreateManager();
            if (!mgr.IsInstalled) return false;

            await mgr.DownloadUpdatesAsync(updateInfo, progress => progressCallback?.Invoke(progress));
            mgr.ApplyUpdatesAndRestart(updateInfo);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            HandleUpdateException("download/install", ex);
            return false;
        }
    }
}