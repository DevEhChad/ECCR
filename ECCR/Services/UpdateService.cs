using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace ECCR.Services;

public class UpdateService
{
    private readonly string _githubRepoUrl;

    public UpdateService(string githubRepoUrl = "https://github.com/ChadDoty/ECCR")
    {
        _githubRepoUrl = githubRepoUrl;
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(_githubRepoUrl, string.Empty, false));
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
            var mgr = new UpdateManager(new GithubSource(_githubRepoUrl, string.Empty, false));
            await mgr.DownloadUpdatesAsync(updateInfo, progressCallback);
            mgr.ApplyUpdatesAndRestart(updateInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}