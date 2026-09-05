using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Binder page's "Bindings" tab visual diagram: a clickable device image with one
/// hotspot per semantic input, live press/stick/pedal highlighting, and capture-on-click -
/// generalized from the old gamepad-only Visual Binder tab to also drive a hand-drawn generic
/// wheel+pedal diagram for wheel/pedal devices (see <see cref="IsWheelDevice"/>). Scoped to
/// whichever device <see cref="BinderPageViewModel.SelectedDeviceName"/> already picks, not a
/// second device selector of its own. Clicking a hotspot arms the exact same physical-press
/// capture flow the Rebinding Table's rows use (<see cref="MainWindowViewModel.StartListeningCommand"/>),
/// so it works for any hardware rather than assuming a fixed physical-index-per-hotspot table.
/// </summary>
public partial class BinderDiagramViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;
    private readonly BinderPageViewModel _parent;

    public static readonly string[] AvailableSkins =
    [
        "Black-White", "Red-White", "Red-White-Faced", "White-Black", "White-Red", "White-Red-Faced"
    ];

    public ObservableCollection<HotspotViewModel> HotspotViewModels { get; }
    public ObservableCollection<WheelHotspotViewModel> WheelHotspotViewModels { get; }

    [ObservableProperty]
    private Bitmap? _skinImage;

    [ObservableProperty]
    private MappingEntry? _selectedHotspotEntry;

    [ObservableProperty] private double _leftStickOffsetX;
    [ObservableProperty] private double _leftStickOffsetY;
    [ObservableProperty] private double _rightStickOffsetX;
    [ObservableProperty] private double _rightStickOffsetY;

    /// <summary>Live rim rotation for the wheel diagram, degrees, positive = turned right. A stylized indicator (fixed sweep), not a literal replica of the wheel's configured rotation range.</summary>
    [ObservableProperty] private double _steeringAngleDegrees;
    [ObservableProperty] private double _throttleFill01;
    [ObservableProperty] private double _brakeFill01;
    [ObservableProperty] private double _clutchFill01;

    public bool HasSelectedHotspot => SelectedHotspotEntry != null;

    /// <summary>The device the graphic reflects - forwarded from the page's shared device list, not owned here.</summary>
    public string? SelectedDeviceName => _parent.SelectedDeviceName;

    public bool IsWheelDevice => SelectedDeviceName != null && DevicePresetService.IsWheelOrPedalCategory(DevicePresetService.DetectCategory(SelectedDeviceName));
    public bool IsGamepadDevice => SelectedDeviceName != null && !IsWheelDevice;

    public string SelectedSkin
    {
        get => _host.ControllerSkin;
        set
        {
            if (_host.ControllerSkin == value) return;
            _host.ControllerSkin = value;
            OnPropertyChanged();
            LoadSkinImage();
        }
    }

    public IRelayCommand<MappingEntry> StartListeningCommand => _host.StartListeningCommand;
    public IRelayCommand<MappingEntry> RemoveMappingCommand => _host.RemoveMappingCommand;

    public BinderDiagramViewModel(MainWindowViewModel host, BinderPageViewModel parent)
    {
        _host = host;
        _parent = parent;

        HotspotViewModels = new ObservableCollection<HotspotViewModel>(
            ControllerHotspotService.Hotspots.Select(h => new HotspotViewModel(h, this)));
        WheelHotspotViewModels = new ObservableCollection<WheelHotspotViewModel>(
            WheelHotspotService.Hotspots.Select(h => new WheelHotspotViewModel(h, this)));

        LoadSkinImage();
        RefreshBoundEntries();

        _parent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BinderPageViewModel.SelectedDeviceName))
            {
                OnPropertyChanged(nameof(SelectedDeviceName));
                OnPropertyChanged(nameof(IsWheelDevice));
                OnPropertyChanged(nameof(IsGamepadDevice));
                SelectedHotspotEntry = null;
                RefreshBoundEntries();
            }
        };

        _host.Mappings.CollectionChanged += (_, _) => RefreshBoundEntries();
        _host.OnLiveDeviceStateUpdated += HandleLiveDeviceState;

        // ControllerSkin's real saved value isn't known yet at the point LoadSkinImage() just
        // ran above - this view model is constructed inside MainWindowViewModel.InitializeShellNavigation(),
        // which runs before MainWindowViewModel.LoadAppSettings() overwrites ControllerSkin from
        // disk. Without this subscription, SkinImage stays baked to the field-initializer
        // default forever while the skin ComboBox (which reads ControllerSkin live) correctly
        // shows the real saved value - the two visibly disagreed until the user manually
        // reselected a skin.
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ControllerSkin))
            {
                LoadSkinImage();
                OnPropertyChanged(nameof(SelectedSkin));
            }
        };
    }

    partial void OnSelectedHotspotEntryChanged(MappingEntry? value) => OnPropertyChanged(nameof(HasSelectedHotspot));

    private void LoadSkinImage()
    {
        SkinImage = new Bitmap(AssetLoader.Open(new Uri($"avares://ECCR/Assets/ControllerSkins/{SelectedSkin}.png")));
    }

    private void RefreshBoundEntries()
    {
        string? deviceName = SelectedDeviceName;
        foreach (var hvm in HotspotViewModels)
        {
            hvm.BoundEntry = _host.Mappings.FirstOrDefault(m =>
                m.SourceDeviceName == deviceName && m.SemanticHotspot == hvm.Hotspot.Semantic.ToString());
        }
        foreach (var wvm in WheelHotspotViewModels)
        {
            wvm.BoundEntry = _host.Mappings.FirstOrDefault(m =>
                m.SourceDeviceName == deviceName && m.SemanticHotspot == wvm.Hotspot.Semantic.ToString());
        }
    }

    /// <summary>
    /// Finds (or creates) the <see cref="MappingEntry"/> for this hotspot on the selected
    /// device, then arms the same physical-press capture the Rebinding Table's rows use. A
    /// freshly created entry deliberately leaves <see cref="MappingEntry.SourceDeviceGuid"/> at
    /// its default <see cref="Guid.Empty"/> rather than resolving it upfront - <c>ProcessInputPolling</c>'s
    /// normal feed-through already pins the GUID by device name on the first poll tick it sees,
    /// same as every "+ Add Input" row does before its first capture.
    /// </summary>
    internal void ArmHotspot(SemanticButton semantic)
    {
        string? deviceName = SelectedDeviceName;
        if (string.IsNullOrEmpty(deviceName)) return;

        var entry = _host.Mappings.FirstOrDefault(m =>
            m.SourceDeviceName == deviceName && m.SemanticHotspot == semantic.ToString());

        if (entry == null)
        {
            entry = new MappingEntry
            {
                SourceDeviceName = deviceName,
                SourceDisplayName = "Click to Bind",
                SemanticHotspot = semantic.ToString(),
                TargetDeviceId = 1,
                TargetOutput = "[Xbox] Xbox A (Cross / South / Handbrake)"
            };
            _host.Mappings.Add(entry);
            _host.RebuildGroupedMappings();
            _host.UpdateVirtualDeviceSummary();
        }

        SelectedHotspotEntry = entry;
        StartListeningCommand.Execute(entry);
    }

    /// <summary>Wheel-diagram counterpart of <see cref="ArmHotspot"/> - same find-or-create-then-capture flow, targeting a sensible "[Wheel] ..." default.</summary>
    internal void ArmWheelHotspot(WheelSemanticInput semantic)
    {
        string? deviceName = SelectedDeviceName;
        if (string.IsNullOrEmpty(deviceName)) return;

        var entry = _host.Mappings.FirstOrDefault(m =>
            m.SourceDeviceName == deviceName && m.SemanticHotspot == semantic.ToString());

        if (entry == null)
        {
            string defaultTarget = semantic switch
            {
                WheelSemanticInput.SteeringAxis => "[Wheel] Steering (Axis X)",
                WheelSemanticInput.ThrottlePedal => "[Wheel] Gas / Throttle (Axis Y)",
                WheelSemanticInput.BrakePedal => "[Wheel] Brake (Axis Z)",
                WheelSemanticInput.ClutchPedal => "[Wheel] Clutch (Axis Rx)",
                WheelSemanticInput.HandbrakeAxis => "[Wheel] Handbrake (Axis Ry)",
                WheelSemanticInput.PaddleLeft => "[Wheel] Paddle Down (Left Shift)",
                WheelSemanticInput.PaddleRight => "[Wheel] Paddle Up (Right Shift)",
                _ => "[Wheel] Button 1"
            };

            entry = new MappingEntry
            {
                SourceDeviceName = deviceName,
                SourceDisplayName = "Click to Bind",
                SemanticHotspot = semantic.ToString(),
                TargetDeviceId = 1,
                TargetOutput = defaultTarget
            };
            _host.Mappings.Add(entry);
            _host.RebuildGroupedMappings();
            _host.UpdateVirtualDeviceSummary();
        }

        SelectedHotspotEntry = entry;
        StartListeningCommand.Execute(entry);
    }

    private void HandleLiveDeviceState(LiveDeviceInputState state)
    {
        if (!string.Equals(state.DeviceName, SelectedDeviceName, StringComparison.OrdinalIgnoreCase)) return;

        var category = DevicePresetService.DetectCategory(state.DeviceName);

        if (IsWheelDevice)
        {
            if (state.NormalizedAxes.Length > 0) SteeringAngleDegrees = (state.NormalizedAxes[0] - 0.5) * 2 * 180;
            if (state.NormalizedAxes.Length > 1) ThrottleFill01 = state.NormalizedAxes[1];
            if (state.NormalizedAxes.Length > 2) BrakeFill01 = state.NormalizedAxes[2];
            if (state.NormalizedAxes.Length > 3) ClutchFill01 = state.NormalizedAxes[3];

            foreach (var wvm in WheelHotspotViewModels)
            {
                int? idx = WheelHotspotService.GetWheelSemanticIndex(category, wvm.Hotspot.Semantic);
                wvm.IsPressed = idx.HasValue && idx.Value < state.Buttons.Length && state.Buttons[idx.Value];
            }
            return;
        }

        var leftWell = ControllerHotspotService.LeftStickWell;
        LeftStickOffsetX = Math.Clamp((state.LeftStickX - 0.5) * 2 * leftWell.TravelRadiusPixels, -leftWell.TravelRadiusPixels, leftWell.TravelRadiusPixels);
        LeftStickOffsetY = Math.Clamp((state.LeftStickY - 0.5) * 2 * leftWell.TravelRadiusPixels, -leftWell.TravelRadiusPixels, leftWell.TravelRadiusPixels);

        var rightWell = ControllerHotspotService.RightStickWell;
        RightStickOffsetX = Math.Clamp((state.RightStickX - 0.5) * 2 * rightWell.TravelRadiusPixels, -rightWell.TravelRadiusPixels, rightWell.TravelRadiusPixels);
        RightStickOffsetY = Math.Clamp((state.RightStickY - 0.5) * 2 * rightWell.TravelRadiusPixels, -rightWell.TravelRadiusPixels, rightWell.TravelRadiusPixels);

        foreach (var hvm in HotspotViewModels)
        {
            int? idx = ControllerHotspotService.GetSemanticButtonIndex(category, hvm.Hotspot.Semantic);
            hvm.IsPressed = idx.HasValue && idx.Value < state.Buttons.Length && state.Buttons[idx.Value];
        }
    }
}

/// <summary>One clickable/highlightable button region on the Binder diagram's gamepad skin image.</summary>
public partial class HotspotViewModel : ObservableObject
{
    public ControllerHotspot Hotspot { get; }
    public IRelayCommand ArmCommand { get; }

    /// <summary>
    /// Flat copies of <see cref="Models.ControllerHotspot.PixelBounds"/>'s components - Avalonia's
    /// compiled bindings can't resolve a nested member path through a value-type (<see cref="Avalonia.Rect"/>)
    /// property reliably, which silently produced zero-sized, invisible, non-hit-testable hotspot
    /// buttons when the DataTemplate bound directly to <c>Hotspot.PixelBounds.X</c> etc.
    /// </summary>
    public double Left => Hotspot.PixelBounds.X;
    public double Top => Hotspot.PixelBounds.Y;
    public double BoundsWidth => Hotspot.PixelBounds.Width;
    public double BoundsHeight => Hotspot.PixelBounds.Height;

    [ObservableProperty]
    private bool _isPressed;

    [ObservableProperty]
    private MappingEntry? _boundEntry;

    public bool IsBound => BoundEntry != null;

    public string TooltipText => BoundEntry != null
        ? $"{Hotspot.Label}\nBound to: {BoundEntry.TargetOutput}"
        : $"{Hotspot.Label}\nNot bound - click to bind";

    public HotspotViewModel(ControllerHotspot hotspot, BinderDiagramViewModel parent)
    {
        Hotspot = hotspot;
        ArmCommand = new RelayCommand(() => parent.ArmHotspot(hotspot.Semantic));
    }

    partial void OnBoundEntryChanged(MappingEntry? value)
    {
        OnPropertyChanged(nameof(IsBound));
        OnPropertyChanged(nameof(TooltipText));
    }
}

/// <summary>Wheel-diagram counterpart of <see cref="HotspotViewModel"/> - mirrors it 1:1 against <see cref="WheelHotspot"/> instead of <see cref="ControllerHotspot"/>.</summary>
public partial class WheelHotspotViewModel : ObservableObject
{
    public WheelHotspot Hotspot { get; }
    public IRelayCommand ArmCommand { get; }

    public double Left => Hotspot.PixelBounds.X;
    public double Top => Hotspot.PixelBounds.Y;
    public double BoundsWidth => Hotspot.PixelBounds.Width;
    public double BoundsHeight => Hotspot.PixelBounds.Height;

    [ObservableProperty]
    private bool _isPressed;

    [ObservableProperty]
    private MappingEntry? _boundEntry;

    public bool IsBound => BoundEntry != null;

    public string TooltipText => BoundEntry != null
        ? $"{Hotspot.Label}\nBound to: {BoundEntry.TargetOutput}"
        : $"{Hotspot.Label}\nNot bound - click to bind";

    public WheelHotspotViewModel(WheelHotspot hotspot, BinderDiagramViewModel parent)
    {
        Hotspot = hotspot;
        ArmCommand = new RelayCommand(() => parent.ArmWheelHotspot(hotspot.Semantic));
    }

    partial void OnBoundEntryChanged(MappingEntry? value)
    {
        OnPropertyChanged(nameof(IsBound));
        OnPropertyChanged(nameof(TooltipText));
    }
}
