using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ECCR.Converters;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.Views;

/// <summary>
/// A real modal child window (opened via <c>ShowDialog</c> from
/// <see cref="ECCR.ViewModels.MainWindowViewModel.OpenCalibration"/>), unlike most of the
/// app's other dialogs - see <see cref="MainWindow"/>'s doc comment. Its DataContext is set
/// directly to the <see cref="ECCR.Models.MappingEntry"/> being calibrated (not a view
/// model), so every binding in the XAML - including the wheel-dial/pedal-bar visualizers -
/// reads and writes that single entry's calibration properties live while the poll loop
/// keeps updating <c>LatestRawReading</c>/<c>LiveOutputPercentage</c> in the background.
/// </summary>
public partial class CalibrationDialog : Window
{
    /// <summary>
    /// The calibrated entry's device-level wheel settings, supplied by
    /// <see cref="ECCR.ViewModels.MainWindowViewModel.OpenCalibration"/> before <c>ShowDialog</c>
    /// is called - source of truth for the steering dial's rotation range (see
    /// <see cref="Models.WheelDeviceSettings.RotationDegrees"/>), bound via ElementName rather
    /// than DataContext since the DataContext is the plain <see cref="MappingEntry"/> being
    /// calibrated. Null for non-wheel entries (the rotation UI stays hidden in that case anyway).
    /// </summary>
    public WheelDeviceSettings? WheelSettings { get; set; }

    public CalibrationDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateVisualizerMode();
    }

    /// <summary>
    /// Wheel/pedal-category devices get a dedicated steering dial or pedal bar visual;
    /// everything else (gamepads, and any wheel axis without a dedicated visual) keeps
    /// the original generic progress bar.
    /// </summary>
    private void UpdateVisualizerMode()
    {
        if (DataContext is not MappingEntry entry || entry.SourceType != InputType.Axis)
        {
            ShowGenericBar();
            return;
        }

        var category = DevicePresetService.DetectCategory(entry.SourceDeviceName);
        if (!DevicePresetService.IsWheelOrPedalCategory(category))
        {
            ShowGenericBar();
            return;
        }

        string target = entry.TargetOutput;
        bool isSteering = target.Contains("Steering", StringComparison.OrdinalIgnoreCase) ||
                           target.Contains("Steer", StringComparison.OrdinalIgnoreCase);
        bool isPedal = target.Contains("Throttle", StringComparison.OrdinalIgnoreCase) ||
                       target.Contains("Gas", StringComparison.OrdinalIgnoreCase) ||
                       target.Contains("Brake", StringComparison.OrdinalIgnoreCase) ||
                       target.Contains("Clutch", StringComparison.OrdinalIgnoreCase) ||
                       target.Contains("Handbrake", StringComparison.OrdinalIgnoreCase);

        if (isSteering)
        {
            GenericOutputBar.IsVisible = false;
            SteeringWheelPanel.IsVisible = true;
            PedalBarPanel.IsVisible = false;
            WheelRotationPanel.IsVisible = true;
            WheelRotationDivider.IsVisible = true;
        }
        else if (isPedal)
        {
            GenericOutputBar.IsVisible = false;
            SteeringWheelPanel.IsVisible = false;
            PedalBarPanel.IsVisible = true;
            PedalFill.Background = PedalColorConverter.Resolve(target);
            WheelRotationPanel.IsVisible = false;
            WheelRotationDivider.IsVisible = false;
        }
        else
        {
            ShowGenericBar();
        }
    }

    private void ShowGenericBar()
    {
        GenericOutputBar.IsVisible = true;
        SteeringWheelPanel.IsVisible = false;
        PedalBarPanel.IsVisible = false;
        WheelRotationPanel.IsVisible = false;
        WheelRotationDivider.IsVisible = false;
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