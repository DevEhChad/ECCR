using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private readonly HashSet<MappingEntry> _trackedMappings = new();

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

        // Per-entry PropertyChanged subscriptions (not just Mappings.CollectionChanged) so an
        // in-place mutation - ClearMapping's SourceDeviceName reassignment, a TargetOutput edit
        // from the Rebind popup - refreshes the diagram immediately instead of only on the next
        // unrelated add/remove. See OnMappingEntryPropertyChanged's doc comment for why only
        // identity-affecting properties trigger a full RefreshBoundEntries() pass here.
        foreach (var m in _host.Mappings) TrackMapping(m);

        _host.Mappings.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var m in _trackedMappings.ToList()) UntrackMapping(m);
                foreach (var m in _host.Mappings) TrackMapping(m);
            }
            else
            {
                if (e.OldItems != null) foreach (MappingEntry m in e.OldItems) UntrackMapping(m);
                if (e.NewItems != null) foreach (MappingEntry m in e.NewItems) TrackMapping(m);
            }
            RefreshBoundEntries();
        };
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

    private void TrackMapping(MappingEntry m)
    {
        if (_trackedMappings.Add(m)) m.PropertyChanged += OnMappingEntryPropertyChanged;
    }

    private void UntrackMapping(MappingEntry m)
    {
        if (_trackedMappings.Remove(m)) m.PropertyChanged -= OnMappingEntryPropertyChanged;
    }

    /// <summary>
    /// Only identity-affecting properties need a full re-match pass here - a bound hotspot's
    /// TargetOutput/SourceDisplayName changes are instead handled per-hotspot by
    /// HotspotViewModel/WheelHotspotViewModel's own BoundEntry subscription, which updates just
    /// that one hotspot's tooltip without re-walking every hotspot.
    /// </summary>
    private void OnMappingEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MappingEntry.SourceDeviceName) or nameof(MappingEntry.SemanticHotspot)
            or nameof(MappingEntry.SourceIndex) or nameof(MappingEntry.SourceType) or nameof(MappingEntry.SourceDisplayName))
            RefreshBoundEntries();
    }

    /// <summary>
    /// Matches each hotspot to its <see cref="MappingEntry"/> by exact <see cref="MappingEntry.SemanticHotspot"/>
    /// first (set only for entries created by clicking this diagram - see <see cref="ArmHotspot"/>/
    /// <see cref="ArmWheelHotspot"/>), falling back to a physical device+index match (the same
    /// reverse-index lookup <see cref="HandleLiveDeviceState"/> already uses for live press
    /// highlighting) for entries created via the Rebinding Table, Auto-Bind, or a loaded profile
    /// that predates any diagram click - those never get <see cref="MappingEntry.SemanticHotspot"/>
    /// set, so without this fallback they always looked "unbound" here despite being genuinely
    /// bound (per <see cref="MappingEntry.IsBound"/>, the table's own definition).
    /// </summary>
    private void RefreshBoundEntries()
    {
        string? deviceName = SelectedDeviceName;
        var category = DevicePresetService.DetectCategory(deviceName ?? string.Empty);

        foreach (var hvm in HotspotViewModels)
        {
            string semanticName = hvm.Hotspot.Semantic.ToString();
            var entry = _host.Mappings.FirstOrDefault(m => m.SourceDeviceName == deviceName && m.SemanticHotspot == semanticName);
            if (entry == null)
            {
                int? physicalIndex = ControllerHotspotService.GetSemanticButtonIndex(category, hvm.Hotspot.Semantic);
                if (physicalIndex.HasValue)
                {
                    entry = _host.Mappings.FirstOrDefault(m =>
                        m.SourceDeviceName == deviceName && m.IsBound &&
                        m.SourceType == InputType.Button && m.SourceIndex == physicalIndex.Value);
                }
            }
            hvm.BoundEntry = entry;
        }

        foreach (var wvm in WheelHotspotViewModels)
        {
            string semanticName = wvm.Hotspot.Semantic.ToString();
            var entry = _host.Mappings.FirstOrDefault(m => m.SourceDeviceName == deviceName && m.SemanticHotspot == semanticName);
            if (entry == null)
            {
                int? physicalIndex = WheelHotspotService.GetWheelSemanticIndex(category, wvm.Hotspot.Semantic);
                if (physicalIndex.HasValue)
                {
                    var expectedType = wvm.Hotspot.IsAxis ? InputType.Axis : InputType.Button;
                    entry = _host.Mappings.FirstOrDefault(m =>
                        m.SourceDeviceName == deviceName && m.IsBound &&
                        m.SourceType == expectedType && m.SourceIndex == physicalIndex.Value);
                }
            }
            wvm.BoundEntry = entry;
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
            if (state.NormalizedAxes.Length > 0)
            {
                // Scales with this wheel's own configured DOR (Wheel Settings tab), same formula
                // Converters/SteeringAngleConverter.cs already uses for the separate Calibration
                // dialog's dial - previously hardcoded to a fixed +-180 regardless of DOR, so a
                // direct-drive wheel configured for e.g. 1800/2700 degrees never showed more than
                // one physical turn's worth of visual rotation here.
                int rotationDegrees = _host.GetOrCreateWheelSettings(state.DeviceName).RotationDegrees;
                SteeringAngleDegrees = (state.NormalizedAxes[0] - 0.5) * rotationDegrees;
            }
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

    /// <summary>Mirrors <see cref="BoundEntry"/>'s own <see cref="MappingEntry.IsArmed"/> - drives this hotspot's "armed" ring when it (or the matching Rebinding Table row) is currently being captured.</summary>
    [ObservableProperty]
    private bool _isArmed;

    public bool IsBound => BoundEntry != null;

    public string TooltipText => BoundEntry != null
        ? $"{Hotspot.Label}\nBound to: {BoundEntry.TargetOutput}"
        : $"{Hotspot.Label}\nNot bound - click to bind";

    /// <summary>Whichever entry <see cref="BoundEntry"/> was pointed at last, so <see cref="OnBoundEntryChanged"/> can unsubscribe from it - the generated single-argument partial method doesn't expose the old value.</summary>
    private MappingEntry? _subscribedEntry;

    public HotspotViewModel(ControllerHotspot hotspot, BinderDiagramViewModel parent)
    {
        Hotspot = hotspot;
        ArmCommand = new RelayCommand(() => parent.ArmHotspot(hotspot.Semantic));
    }

    partial void OnBoundEntryChanged(MappingEntry? value)
    {
        if (_subscribedEntry != null) _subscribedEntry.PropertyChanged -= OnBoundEntryPropertyChanged;
        _subscribedEntry = value;
        if (_subscribedEntry != null) _subscribedEntry.PropertyChanged += OnBoundEntryPropertyChanged;

        IsArmed = value?.IsArmed ?? false;
        OnPropertyChanged(nameof(IsBound));
        OnPropertyChanged(nameof(TooltipText));
    }

    /// <summary>Live-updates this one hotspot's tooltip/armed state on an in-place edit to its bound entry, without needing a full RefreshBoundEntries() pass.</summary>
    private void OnBoundEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MappingEntry.TargetOutput):
            case nameof(MappingEntry.SourceDisplayName):
                OnPropertyChanged(nameof(TooltipText));
                break;
            case nameof(MappingEntry.IsArmed):
                IsArmed = BoundEntry?.IsArmed ?? false;
                break;
        }
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

    /// <summary>Mirrors <see cref="BoundEntry"/>'s own <see cref="MappingEntry.IsArmed"/> - drives this hotspot's "armed" ring when it (or the matching Rebinding Table row) is currently being captured.</summary>
    [ObservableProperty]
    private bool _isArmed;

    public bool IsBound => BoundEntry != null;

    public string TooltipText => BoundEntry != null
        ? $"{Hotspot.Label}\nBound to: {BoundEntry.TargetOutput}"
        : $"{Hotspot.Label}\nNot bound - click to bind";

    /// <summary>Whichever entry <see cref="BoundEntry"/> was pointed at last, so <see cref="OnBoundEntryChanged"/> can unsubscribe from it - the generated single-argument partial method doesn't expose the old value.</summary>
    private MappingEntry? _subscribedEntry;

    public WheelHotspotViewModel(WheelHotspot hotspot, BinderDiagramViewModel parent)
    {
        Hotspot = hotspot;
        ArmCommand = new RelayCommand(() => parent.ArmWheelHotspot(hotspot.Semantic));
    }

    partial void OnBoundEntryChanged(MappingEntry? value)
    {
        if (_subscribedEntry != null) _subscribedEntry.PropertyChanged -= OnBoundEntryPropertyChanged;
        _subscribedEntry = value;
        if (_subscribedEntry != null) _subscribedEntry.PropertyChanged += OnBoundEntryPropertyChanged;

        IsArmed = value?.IsArmed ?? false;
        OnPropertyChanged(nameof(IsBound));
        OnPropertyChanged(nameof(TooltipText));
    }

    /// <summary>Live-updates this one hotspot's tooltip/armed state on an in-place edit to its bound entry, without needing a full RefreshBoundEntries() pass.</summary>
    private void OnBoundEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MappingEntry.TargetOutput):
            case nameof(MappingEntry.SourceDisplayName):
                OnPropertyChanged(nameof(TooltipText));
                break;
            case nameof(MappingEntry.IsArmed):
                IsArmed = BoundEntry?.IsArmed ?? false;
                break;
        }
    }
}
