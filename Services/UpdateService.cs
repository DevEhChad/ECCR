using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace ECCR.Services;

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