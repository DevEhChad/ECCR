using System;
using Avalonia;
using Velopack;

namespace ECCR;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // Handles installer hooks (--veloapp-install, --veloapp-updated, etc.)
            VelopackApp.Build().Run();
        }
        catch
        {
            // Prevent hook exceptions from blocking application execution
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}