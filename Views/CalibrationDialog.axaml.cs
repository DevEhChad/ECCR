using Avalonia.Controls;
using Avalonia.Interactivity;
using ECCR.Models;

namespace ECCR.Views;

public partial class CalibrationDialog : Window
{
    public CalibrationDialog()
    {
        InitializeComponent();
    }

    private void OnSetMinClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MappingEntry entry)
        {
            entry.RawMin = entry.LatestRawReading;
        }
    }

    private void OnSetCenterClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MappingEntry entry)
        {
            entry.RawCenter = entry.LatestRawReading;
        }
    }

    private void OnSetMaxClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MappingEntry entry)
        {
            entry.RawMax = entry.LatestRawReading;
        }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MappingEntry entry)
        {
            entry.RawMin = 0;
            entry.RawMax = 65535;
            entry.RawCenter = 32767;
            entry.Deadzone = 0.0;
            entry.IsInverted = false;
        }
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}