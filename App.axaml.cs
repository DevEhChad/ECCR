using System;
using System.IO;
using System.Linq;
using System.Threading;
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
    private static App? _instance;
    private MainWindowViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "crash.log");

    public App()
    {
        _instance = this;
    }

    /// <summary>
    /// Last-resort handler for exceptions that escape every other try/catch in the app (wired
    /// up in <see cref="Program.Main"/> via AppDomain.UnhandledException /
    /// TaskScheduler.UnobservedTaskException). Logs the failure and, when the process is about
    /// to die, makes a best-effort attempt to release driver handles (HidHide cloaking, vJoy,
    /// ViGEm, the exclusive wheel FFB handle) rather than leaving them orphaned.
    /// </summary>
    public static void HandleFatalException(string source, Exception? ex, bool attemptCleanup)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}: {ex}\n");
        }
        catch { }

        if (!attemptCleanup) return;

        RunCleanupWithWatchdog(_instance?._mainViewModel);
    }

    /// <summary>
    /// Runs <see cref="MainWindowViewModel.CleanupAndShutdown"/> with a hard time limit. Every
    /// individual step inside it is already try/catch'd and bounded (see
    /// <c>MainWindowViewModel.RunShutdownStep</c>), but this is the last-resort guarantee that
    /// the app actually goes away if some future change to that chain (or a misbehaving driver
    /// call) ever blocks indefinitely: a background watchdog force-kills the process a few
    /// seconds after this is called if it hasn't already exited by then.
    /// </summary>
    private static void RunCleanupWithWatchdog(MainWindowViewModel? vm)
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(5000);
            Environment.Exit(1);
        })
        { IsBackground = true, Name = "ShutdownWatchdog" };
        watchdog.Start();

        try
        {
            if (vm != null)
            {
                vm.IsShuttingDown = true;
                vm.CleanupAndShutdown();
            }
        }
        catch { }
    }

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

                // Respect the "Run in System Tray" setting instead of always creating the icon.
                if (_mainViewModel.RunInSystemTray)
                {
                    InitializeSystemTray();
                }

                _mainViewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.RunInSystemTray))
                    {
                        SetTrayIconVisible(_mainViewModel.RunInSystemTray);
                    }
                };

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

            _trayIcon = trayIcon;
            var trayIcons = new TrayIcons { trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
        }
        catch
        {
            // Suppress fallback errors if OS tray is unavailable
        }
    }

    /// <summary>
    /// Adds or removes the tray icon live when the user flips "Run in System Tray" in Settings,
    /// instead of only taking effect on next launch.
    /// </summary>
    private void SetTrayIconVisible(bool visible)
    {
        try
        {
            if (visible)
            {
                if (_trayIcon == null) InitializeSystemTray();
            }
            else if (_trayIcon != null)
            {
                TrayIcon.SetIcons(this, new TrayIcons());
                _trayIcon = null;
            }
        }
        catch { }
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

    private void PerformFullExit() => PerformFullExitFor(_mainViewModel);

    /// <summary>
    /// The one real-quit path, shared by the main window's close (X) button, the tray's Exit
    /// item, and app-lifetime Exit. ViGEm/vJoy/HidHide and the DirectInput polling loop hold
    /// native handles and a background thread that don't always unwind cleanly through a
    /// normal managed shutdown. RunCleanupWithWatchdog's background timer guarantees the
    /// process actually dies even if cleanup somehow blocks; this Environment.Exit is the
    /// normal-path exit once cleanup has had a chance to run.
    /// </summary>
    public static void PerformFullExitFor(MainWindowViewModel? vm)
    {
        RunCleanupWithWatchdog(vm);
        Environment.Exit(0);
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