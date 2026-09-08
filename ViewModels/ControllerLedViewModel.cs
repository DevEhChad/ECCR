using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Controller LED page: lightbar color/animation control for Sony gamepads only.
/// Reuses <see cref="DeviceStatusBarViewModel.GamepadStatuses"/> for the device picker (same
/// shared instance as the status bar/Controllers page) so "supported" reflects live
/// <see cref="ControllerStatus.IsLedSupported"/> state rather than a static device-name guess.
/// The chosen color/mode is persisted per-profile (see <see cref="ECCR.Models.UserProfile.LedRed"/>/
/// <c>LedGreen</c>/<c>LedBlue</c>/<c>LedMode</c>/<c>LedSpeed</c>) via <see cref="_onColorChanged"/>,
/// an injected save callback (same one-way-dependency style <see cref="AutoMapWizardViewModel"/>
/// uses for <c>_onMappingsApplied</c>) rather than this VM reaching back into
/// <see cref="MainWindowViewModel"/> directly.
/// <see cref="Red"/>/<see cref="Green"/>/<see cref="Blue"/> always hold the user's chosen base
/// color (what the sliders/hex box show and what gets saved); non-<see cref="LedEffectMode.Static"/>
/// modes compute an animated color from that base on a background timer (<see cref="_effectTimer"/>)
/// without touching those properties, so the picker never fights the animation.
/// </summary>
public partial class ControllerLedViewModel : ViewModelBase, IDisposable
{
    private readonly ControllerHidService? _hidService;
    private readonly Action _onColorChanged;
    private bool _isRestoringColor;
    private bool _isSyncingHex;

    private Timer? _effectTimer;
    private readonly Stopwatch _effectStopwatch = new();
    private bool _disposed;

    public ObservableCollection<ControllerStatus> GamepadDevices { get; }

    public static Array EffectModes => Enum.GetValues(typeof(LedEffectMode));

    [ObservableProperty]
    private ControllerStatus? _selectedDevice;

    [ObservableProperty]
    private int _red = 0;

    [ObservableProperty]
    private int _green = 122;

    [ObservableProperty]
    private int _blue = 255;

    [ObservableProperty]
    private string _hexInput = "#0078FF";

    [ObservableProperty]
    private LedEffectMode _effectMode = LedEffectMode.Static;

    /// <summary>Animation rate for every non-Static mode: 0.2 (slow) to 3.0 (fast), default 1.0.</summary>
    [ObservableProperty]
    private double _effectSpeed = 1.0;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>The color actually shown in the preview swatch - the base color while Static, the live animated color otherwise.</summary>
    [ObservableProperty]
    private Color _displayColor = Color.FromRgb(0, 122, 255);

    public bool IsSupportedForSelectedDevice => SelectedDevice?.IsLedSupported ?? false;

    public bool IsEffectModeActive => EffectMode != LedEffectMode.Static;

    public IBrush PreviewBrush => new SolidColorBrush(DisplayColor);

    /// <summary>
    /// The per-device settings object <see cref="Red"/>/<see cref="Green"/>/<see cref="Blue"/>/
    /// <see cref="EffectMode"/>/<see cref="EffectSpeed"/> mirror into as the user edits them (see
    /// <see cref="SetActiveDevice"/>) - null when no gamepad is selected (or the selected device
    /// is a wheel, which has no LED settings). Writing into it is what actually persists the
    /// color per-device: <c>GamepadDeviceSettings</c> is an <c>ObservableObject</c> that
    /// <c>MainWindowViewModel.OnGamepadSettingsPropertyChanged</c> already auto-saves on change.
    /// </summary>
    public GamepadDeviceSettings? ActiveDeviceSettings { get; private set; }

    /// <summary>
    /// Retargets this view model at the given gamepad and its per-device LED settings - called
    /// by <c>DeviceMappingPageViewModel</c> whenever the Device Mappings page's selected device changes, since
    /// LED settings moved from one profile-wide color to per-device (see
    /// <see cref="ActiveDeviceSettings"/>). Loads <paramref name="settings"/>' saved color/mode
    /// while suppressing the mirror-back-into-settings writes below, the same way
    /// <see cref="SetColorFromProfile"/> already does for the old profile-wide load path.
    /// </summary>
    public void SetActiveDevice(ControllerStatus? device, GamepadDeviceSettings? settings)
    {
        if (device != null && GamepadDevices.Contains(device)) SelectedDevice = device;
        ActiveDeviceSettings = settings;

        if (settings != null)
        {
            SetColorFromProfile(settings.LedRed, settings.LedGreen, settings.LedBlue, settings.LedMode, settings.LedSpeed);
        }
    }

    partial void OnRedChanged(int value)
    {
        SyncHexFromRgb();
        RefreshStaticPreview();
        if (!_isRestoringColor)
        {
            if (ActiveDeviceSettings != null) ActiveDeviceSettings.LedRed = (byte)Math.Clamp(value, 0, 255);
            _onColorChanged();
        }
    }

    partial void OnGreenChanged(int value)
    {
        SyncHexFromRgb();
        RefreshStaticPreview();
        if (!_isRestoringColor)
        {
            if (ActiveDeviceSettings != null) ActiveDeviceSettings.LedGreen = (byte)Math.Clamp(value, 0, 255);
            _onColorChanged();
        }
    }

    partial void OnBlueChanged(int value)
    {
        SyncHexFromRgb();
        RefreshStaticPreview();
        if (!_isRestoringColor)
        {
            if (ActiveDeviceSettings != null) ActiveDeviceSettings.LedBlue = (byte)Math.Clamp(value, 0, 255);
            _onColorChanged();
        }
    }

    partial void OnEffectModeChanged(LedEffectMode value)
    {
        OnPropertyChanged(nameof(IsEffectModeActive));
        RestartEffectEngine();
        if (!_isRestoringColor)
        {
            if (ActiveDeviceSettings != null) ActiveDeviceSettings.LedMode = value;
            _onColorChanged();
        }
    }

    partial void OnEffectSpeedChanged(double value)
    {
        if (!_isRestoringColor)
        {
            if (ActiveDeviceSettings != null) ActiveDeviceSettings.LedSpeed = value;
            _onColorChanged();
        }
    }

    /// <summary>Design-time constructor.</summary>
    public ControllerLedViewModel() : this(new ObservableCollection<ControllerStatus>(), null, () => { })
    {
    }

    public ControllerLedViewModel(ObservableCollection<ControllerStatus> gamepadDevices, ControllerHidService? hidService, Action onColorChanged)
    {
        GamepadDevices = gamepadDevices;
        _hidService = hidService;
        _onColorChanged = onColorChanged;
        GamepadDevices.CollectionChanged += (_, _) => AutoSelectDeviceIfNeeded();
        AutoSelectDeviceIfNeeded();
        RefreshStaticPreview();
    }

    /// <summary>
    /// Sets the color/mode while suppressing <see cref="_onColorChanged"/> (the profile save
    /// callback would otherwise fire mid-load, and pointlessly several times over) - used when
    /// restoring a profile's saved LED state.
    /// </summary>
    public void SetColorFromProfile(byte r, byte g, byte b, LedEffectMode mode = LedEffectMode.Static, double speed = 1.0)
    {
        _isRestoringColor = true;
        try
        {
            Red = r;
            Green = g;
            Blue = b;
            EffectSpeed = Math.Clamp(speed, 0.2, 3.0);
            EffectMode = mode;
        }
        finally
        {
            _isRestoringColor = false;
        }
    }

    private void AutoSelectDeviceIfNeeded()
    {
        if (SelectedDevice != null && GamepadDevices.Contains(SelectedDevice)) return;
        SelectedDevice = GamepadDevices.Count > 0 ? GamepadDevices[0] : null;
    }

    partial void OnSelectedDeviceChanged(ControllerStatus? value)
    {
        OnPropertyChanged(nameof(IsSupportedForSelectedDevice));
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void ApplyColor()
    {
        if (SelectedDevice == null || !SelectedDevice.IsLedSupported || _hidService == null)
        {
            StatusMessage = "Not supported for this device.";
            return;
        }

        if (EffectMode != LedEffectMode.Static)
        {
            // An effect is already driving the lightbar continuously; nothing more to do
            // beyond confirming it's running for the user.
            StatusMessage = $"{EffectMode} effect running.";
            return;
        }

        bool ok = _hidService.SetLightbarColor(SelectedDevice.VendorId, SelectedDevice.ProductId, (byte)Red, (byte)Green, (byte)Blue);
        StatusMessage = ok ? "Lightbar color applied." : "Failed to apply color - device may be disconnected.";
    }

    /// <summary>Parses <see cref="HexInput"/> (accepts "RRGGBB" or "#RRGGBB") into <see cref="Red"/>/<see cref="Green"/>/<see cref="Blue"/>. Invalid input is reported and left unchanged.</summary>
    [RelayCommand]
    private void ApplyHexInput()
    {
        string text = (HexInput ?? string.Empty).Trim().TrimStart('#');

        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed))
        {
            StatusMessage = "Enter a valid hex color, e.g. #FF8800.";
            SyncHexFromRgb();
            return;
        }

        _isSyncingHex = true;
        try
        {
            Red = (int)((parsed >> 16) & 0xFF);
            Green = (int)((parsed >> 8) & 0xFF);
            Blue = (int)(parsed & 0xFF);
        }
        finally
        {
            _isSyncingHex = false;
        }

        SyncHexFromRgb();
    }

    private void SyncHexFromRgb()
    {
        if (_isSyncingHex) return;
        HexInput = $"#{Red:X2}{Green:X2}{Blue:X2}";
    }

    private void RefreshStaticPreview()
    {
        OnPropertyChanged(nameof(PreviewBrush));
        if (EffectMode == LedEffectMode.Static)
        {
            DisplayColor = Color.FromRgb((byte)Red, (byte)Green, (byte)Blue);
        }
    }

    /// <summary>
    /// Starts/stops the background animation timer to match <see cref="EffectMode"/>. Static
    /// mode has no timer running - the picked color is only pushed to hardware via
    /// <see cref="ApplyColorCommand"/>, same as before effects existed. Any other mode starts a
    /// ~30fps timer (<see cref="EffectTick"/>) that keeps writing to the device on its own.
    /// </summary>
    private void RestartEffectEngine()
    {
        _effectTimer?.Dispose();
        _effectTimer = null;

        if (EffectMode == LedEffectMode.Static)
        {
            RefreshStaticPreview();
            return;
        }

        _effectStopwatch.Restart();
        _effectTimer = new Timer(_ => EffectTick(), null, dueTime: 0, period: 33);
    }

    private void EffectTick()
    {
        if (_disposed) return;
        if (SelectedDevice == null || !SelectedDevice.IsLedSupported || _hidService == null) return;

        double t = _effectStopwatch.Elapsed.TotalSeconds;
        double speed = Math.Clamp(EffectSpeed, 0.2, 3.0);
        byte baseR = (byte)Red, baseG = (byte)Green, baseB = (byte)Blue;

        (byte r, byte g, byte b) color = EffectMode switch
        {
            LedEffectMode.Breathing => ScaleBrightness(baseR, baseG, baseB, 0.05 + 0.95 * (0.5 + 0.5 * Math.Sin(t * speed * Math.PI))),
            LedEffectMode.Pulse => ScaleBrightness(baseR, baseG, baseB, 0.03 + 0.97 * Math.Pow(0.5 + 0.5 * Math.Sin(t * speed * 2.0 * Math.PI), 3.0)),
            LedEffectMode.Rainbow => HsvToRgb((t * speed * 80.0) % 360.0, 1.0, 1.0),
            LedEffectMode.ColorCycle => RainbowPalette[(int)(t * speed) % RainbowPalette.Length],
            LedEffectMode.Strobe => ((t * speed * 4.0) % 2.0) < 1.0 ? (baseR, baseG, baseB) : ((byte)0, (byte)0, (byte)0),
            _ => (baseR, baseG, baseB)
        };

        try { _hidService.SetLightbarColor(SelectedDevice.VendorId, SelectedDevice.ProductId, color.r, color.g, color.b); }
        catch { }

        Dispatcher.UIThread.Post(() =>
        {
            DisplayColor = Color.FromRgb(color.r, color.g, color.b);
            OnPropertyChanged(nameof(PreviewBrush));
        });
    }

    private static readonly (byte, byte, byte)[] RainbowPalette =
    [
        (255, 0, 0), (255, 128, 0), (255, 255, 0), (0, 255, 0),
        (0, 255, 255), (0, 96, 255), (160, 0, 255), (255, 0, 200)
    ];

    private static (byte, byte, byte) ScaleBrightness(byte r, byte g, byte b, double factor)
    {
        factor = Math.Clamp(factor, 0.0, 1.0);
        return ((byte)(r * factor), (byte)(g * factor), (byte)(b * factor));
    }

    /// <summary>Standard HSV-&gt;RGB conversion (h in degrees 0-360, s/v 0-1) used by the Rainbow effect.</summary>
    private static (byte, byte, byte) HsvToRgb(double h, double s, double v)
    {
        h %= 360.0;
        if (h < 0) h += 360.0;

        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;

        (double r1, double g1, double b1) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return ((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _effectTimer?.Dispose();
        _effectTimer = null;
    }
}
