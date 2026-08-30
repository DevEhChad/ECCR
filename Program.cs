using System;
using Avalonia;
using Velopack;

namespace ECCR;

/// <summary>Native entry point. Sets up Velopack (auto-update) and boots the Avalonia UI.</summary>
internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before anything else: on first launch after an install/update, Velopack
        // intercepts special "--squirrel-*" style startup args (install/uninstall/update
        // hooks) and may exit the process immediately without ever reaching the UI.
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}