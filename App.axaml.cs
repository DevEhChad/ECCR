using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using ECCR.ViewModels;
using ECCR.Views;

namespace ECCR;

/// <summary>
/// Application lifetime root. Owns the single <see cref="MainWindowViewModel"/> instance for
/// the process, wires up the system tray icon, and coordinates minimize-to-tray /
/// close-to-tray behavior with <see cref="Views.MainWindow"/>.
/// </summary>
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
                
                // Initialize tray icon at runtime only
                InitializeSystemTray();

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

    private void InitializeSystemTray()
    {
        try
        {
            var trayIcon = new TrayIcon
            {
                ToolTipText = "EhChads Controller Remapper"
            };

            var iconUri = new Uri("avares://ECCR/Assets/ECCR-logo.ico");
            if (AssetLoader.Exists(iconUri))
            {
                trayIcon.Icon = new WindowIcon(AssetLoader.Open(iconUri));
            }

            trayIcon.Clicked += OnTrayIconClicked;

            var menu = new NativeMenu();
            
            var openItem = new NativeMenuItem("Open ECCR");
            openItem.Click += OnShowAppClick;
            
            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += OnExitAppClick;

            menu.Add(openItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exitItem);

            trayIcon.Menu = menu;

            var trayIcons = new TrayIcons { trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
        }
        catch
        {
            // Suppress fallback errors if OS tray is unavailable
        }
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
            // ViGEm/vJoy/HidHide and the DirectInput polling loop hold native handles and a
            // background thread that don't always unwind cleanly through a normal managed
            // shutdown. Environment.Exit forces immediate process termination once cleanup
            // has had a chance to run, instead of risking a hang on exit.
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