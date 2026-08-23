using System;
using Avalonia;
using Velopack;

namespace ECCR;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Handles install/uninstall hooks and desktop shortcuts before UI initializes
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}