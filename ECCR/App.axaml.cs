using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ECCR.ViewModels;
using ECCR.Views;

namespace ECCR;

public partial class App : Application
{
    private MainWindowViewModel? _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainViewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };

            desktop.Exit += (sender, args) =>
            {
                _mainViewModel?.CleanupAndShutdown();
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                _mainViewModel?.CleanupAndShutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    public void OnShowAppClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    public void OnExitAppClick(object? sender, EventArgs e)
    {
        _mainViewModel?.CleanupAndShutdown();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            desktop.MainWindow.Show();
            desktop.MainWindow.WindowState = WindowState.Normal;
            desktop.MainWindow.Activate();
        }
    }
}