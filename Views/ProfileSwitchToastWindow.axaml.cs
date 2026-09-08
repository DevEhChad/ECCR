using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ECCR.Models;

namespace ECCR.Views;

/// <summary>Plain data bag for the toast's bindings - see ProfileSwitchToastWindow.axaml's doc comment for why this isn't a full view model.</summary>
public class ToastContent
{
    public string GameDisplayName { get; }
    public string? BannerImagePath { get; }
    public string ProfileName { get; }

    public ToastContent(string gameDisplayName, string? bannerImagePath, string profileName)
    {
        GameDisplayName = gameDisplayName;
        BannerImagePath = bannerImagePath;
        ProfileName = profileName;
    }
}

public partial class ProfileSwitchToastWindow : Window
{
    private const double VisibleDurationSeconds = 3.5;
    private const double FadeDurationSeconds = 0.3;

    private DispatcherTimer? _dismissTimer;
    private DispatcherTimer? _closeTimer;

    public ProfileSwitchToastWindow()
    {
        InitializeComponent();
    }

    /// <summary>Populates content, positions the window at the primary screen's bottom-right corner, shows it, and schedules the fade-out/close - all in one call since this window is entirely one-shot.</summary>
    public void ShowToast(GameInfo game, string profileName)
    {
        DataContext = new ToastContent(game.DisplayName, game.BannerImagePath, profileName);

        Show();

        var workingArea = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        Position = new PixelPoint(
            workingArea.X + workingArea.Width - (int)Width - 24,
            workingArea.Y + workingArea.Height - (int)Height - 24);

        Opacity = 1; // animates via the DoubleTransition declared in XAML

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(VisibleDurationSeconds) };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer?.Stop();
            Opacity = 0;

            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(FadeDurationSeconds) };
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer?.Stop();
                Close();
            };
            _closeTimer.Start();
        };
        _dismissTimer.Start();
    }
}
