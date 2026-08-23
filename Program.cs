using System;
using Avalonia;
using Velopack;

namespace ECCR;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first before any UI framework initializes
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}