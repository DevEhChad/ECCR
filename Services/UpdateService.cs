using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace ECCR.Services;

public class UpdateService
{
    private const string RepoUrl = "https://github.com/DevEhChad/ECCR";

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var source = new GithubUpdateSource(RepoUrl, accessToken: null, prerelease: false);
            var mgr = new UpdateManager(source);

            // If running in development / loose debug folder, skip checking
            if (!mgr.IsInstalled)
            {
                return null;
            }

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
            var source = new GithubUpdateSource(RepoUrl, accessToken: null, prerelease: false);
            var mgr = new UpdateManager(source);

            await mgr.DownloadUpdatesAsync(updateInfo, progress =>
            {
                progressCallback?.Invoke(progress);
            });

            mgr.ApplyUpdatesAndRestart(updateInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}