using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Velopack;

namespace ECCR;

/// <summary>Native entry point. Sets up Velopack (auto-update) and boots the Avalonia UI.</summary>
internal sealed class Program
{
    // Named machine-wide mutex so a second launch (e.g. re-running from an IDE while a prior
    // instance is still resident in the tray) never gets far enough to independently acquire
    // the same exclusive DirectInput wheel handle, vJoy devices, ViGEm pads, or HidHide's
    // shared driver-wide blocklist alongside the first instance.
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Global\\ECCR-SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Another instance already owns the mutex - don't start a second copy that would
            // fight it over exclusive kernel driver handles. Just exit; the existing instance
            // (possibly minimized to tray) keeps running.
            return;
        }

        // Must run before anything else: on first launch after an install/update, Velopack
        // intercepts special "--squirrel-*" style startup args (install/uninstall/update
        // hooks) and may exit the process immediately without ever reaching the UI.
        VelopackApp.Build().Run();

        // Nothing in the app has a global safety net otherwise: an exception escaping any
        // background Timer/Task callback (or the UI thread) would previously crash the process
        // with zero diagnostics and skip every driver-cleanup path. These are the last line of
        // defense - log what happened and, for exceptions that are actually about to take the
        // process down, make a best-effort attempt to release driver handles first.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            App.HandleFatalException("AppDomain.UnhandledException", e.ExceptionObject as Exception, attemptCleanup: true);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            App.HandleFatalException("TaskScheduler.UnobservedTaskException", e.Exception, attemptCleanup: false);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}