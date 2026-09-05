using Avalonia.Controls;
using Avalonia.Interactivity;
using ECCR.Models;

namespace ECCR.Views;

public partial class BinderPageView : UserControl
{
    public BinderPageView()
    {
        InitializeComponent();
    }

    /// <summary>Calibration tab Set Min/Center/Max buttons - each row's Button.DataContext is the MappingEntry it's calibrating (same pattern as CalibrationDialog.axaml.cs's per-entry versions).</summary>
    private void OnCalSetMinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MappingEntry entry }) entry.RawMin = entry.LatestRawReading;
    }

    private void OnCalSetCenterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MappingEntry entry }) entry.RawCenter = entry.LatestRawReading;
    }

    private void OnCalSetMaxClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MappingEntry entry }) entry.RawMax = entry.LatestRawReading;
    }
}
