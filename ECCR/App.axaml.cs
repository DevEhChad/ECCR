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
    public static MainWindow? AppMainWindow { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppMainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
            desktop.MainWindow = AppMainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (AppMainWindow != null)
            {
                AppMainWindow.AllowFullClose = true;
            }
            desktop.Shutdown();
        }
    }

    public static void ShowMainWindow()
    {
        if (AppMainWindow != null)
        {
            AppMainWindow.Show();
            AppMainWindow.WindowState = WindowState.Normal;
            AppMainWindow.Activate();
        }
    }
}