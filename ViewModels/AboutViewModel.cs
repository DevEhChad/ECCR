using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the About page: the changelog for the CURRENT version only (independent of
/// <see cref="MainWindowViewModel.ChangelogEntries"/>, which keeps serving the post-update
/// popup and Settings' "full history" viewer unchanged) plus a website/repo link row.
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    public ObservableCollection<ChangelogBlock> CurrentVersionEntries { get; } = new();

    [ObservableProperty]
    private string _versionSubtitle = string.Empty;

    /// <summary>Design-time constructor.</summary>
    public AboutViewModel() : this("v1.0.0")
    {
    }

    public AboutViewModel(string appVersion)
    {
        LoadCurrentVersion(appVersion);
    }

    private void LoadCurrentVersion(string appVersion)
    {
        string? markdown = ChangelogService.LoadBundledMarkdown();
        if (markdown == null) return;

        string cleanVersion = appVersion.TrimStart('v', 'V');
        string section = ChangelogService.ExtractVersionSection(markdown, cleanVersion) ?? markdown;

        foreach (var block in ChangelogService.Parse(section)) CurrentVersionEntries.Add(block);

        VersionSubtitle = $"What's new in {appVersion}";
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/DevEhChad/ECCR") { UseShellExecute = true });
        }
        catch { }
    }
}
