using System;
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
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "https://github.com/DevEhChad/ECCR";

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
            return await mgr.CheckForUpdatesAsync();
        }
        catch
        {
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
            return true;
        }
        catch
        {
            return false;
        }
    }
}