using System;
using System.Linq;
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
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Design.IsDesignMode)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = null
                };
            }
            else
            {
                _mainViewModel = new MainWindowViewModel();
                _mainWindow = new MainWindow
                {
                    DataContext = _mainViewModel
                };

                desktop.MainWindow = _mainWindow;
                
                bool startMinimized = desktop.Args != null && desktop.Args.Any(a => a.Contains("minimized", StringComparison.OrdinalIgnoreCase));
                if (startMinimized)
                {
                    _mainWindow.WindowState = WindowState.Minimized;
                }

                desktop.Exit += (sender, args) =>
                {
                    PerformFullExit();
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void OnShowAppClick(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void OnExitAppClick(object? sender, EventArgs e)
    {
        PerformFullExit();
    }

    private void PerformFullExit()
    {
        try
        {
            if (_mainViewModel != null)
            {
                _mainViewModel.IsShuttingDown = true;
                _mainViewModel.CleanupAndShutdown();
            }
        }
        catch { }
        finally
        {
            // Forcefully terminate unmanaged driver threads and exit immediately
            Environment.Exit(0);
        }
    }

    private void ShowWindow()
    {
        if (_mainWindow == null) return;

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }
}