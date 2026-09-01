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
/// Backs the Visual Binder tab: a clickable controller-skin image (swappable "skin", see
/// <see cref="AvailableSkins"/>/<see cref="MainWindowViewModel.ControllerSkin"/>) with one
/// <see cref="HotspotViewModel"/> per <see cref="SemanticButton"/> and two moving analog-stick
/// graphics, all driven by <see cref="MainWindowViewModel.OnLiveDeviceStateUpdated"/>. Scoped to
/// whichever device <see cref="ControllerMappingViewModel.SelectedDeviceName"/> already picks
/// (the same device picker that scopes the Deadzones/Sticks/More Options tabs) rather than
/// owning a second device selector. Clicking a hotspot arms the exact same physical-press
/// capture flow the Button Mapping tab's rows use (<see cref="MainWindowViewModel.StartListeningCommand"/>),
/// so it works for any hardware rather than assuming a fixed physical-index-per-hotspot table.
/// </summary>
public partial class VisualBinderViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;
    private readonly ControllerMappingViewModel _parent;

    public static readonly string[] AvailableSkins =
    [
        "Black-White", "Red-White", "Red-White-Faced", "White-Black", "White-Red", "White-Red-Faced"
    ];

    public ObservableCollection<HotspotViewModel> HotspotViewModels { get; }

    [ObservableProperty]
    private Bitmap? _skinImage;

    [ObservableProperty]
    private MappingEntry? _selectedHotspotEntry;

    [ObservableProperty] private double _leftStickOffsetX;
    [ObservableProperty] private double _leftStickOffsetY;
    [ObservableProperty] private double _rightStickOffsetX;
    [ObservableProperty] private double _rightStickOffsetY;

    public bool HasSelectedHotspot => SelectedHotspotEntry != null;

    /// <summary>The device the graphic reflects - forwarded from the page's shared device picker, not owned here.</summary>
    public string? SelectedDeviceName => _parent.SelectedDeviceName;

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

    public VisualBinderViewModel(MainWindowViewModel host, ControllerMappingViewModel parent)
    {
        _host = host;
        _parent = parent;

        HotspotViewModels = new ObservableCollection<HotspotViewModel>(
            ControllerHotspotService.Hotspots.Select(h => new HotspotViewModel(h, this)));

        LoadSkinImage();
        RefreshBoundEntries();

        _parent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ControllerMappingViewModel.SelectedDeviceName))
            {
                OnPropertyChanged(nameof(SelectedDeviceName));
                RefreshBoundEntries();
            }
        };

        _host.Mappings.CollectionChanged += (_, _) => RefreshBoundEntries();
        _host.OnLiveDeviceStateUpdated += HandleLiveDeviceState;
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
    }

    /// <summary>
    /// Finds (or creates) the <see cref="MappingEntry"/> for this hotspot on the selected
    /// device, then arms the same physical-press capture the Button Mapping tab's rows use. A
    /// freshly created entry deliberately leaves <see cref="MappingEntry.SourceDeviceGuid"/> at
    /// its default <see cref="Guid.Empty"/> rather than resolving it upfront - <c>ProcessInputPolling</c>'s
    /// normal feed-through already pins the GUID by device name on the first poll tick it sees,
    /// same as every "+ New Binding" row does before its first capture.
    /// </summary>
    internal void ArmHotspot(SemanticButton semantic)
    {
        string? deviceName = SelectedDeviceName;
        if (string.IsNullOrEmpty(deviceName)) return;

        var entry = _host.Mappings.FirstOrDefault(m =>
            m.SourceDeviceName == deviceName && m.SemanticHotspot == semantic.ToString());

        if (entry == null)
        {
            var category = DevicePresetService.DetectCategory(deviceName);
            entry = new MappingEntry
            {
                SourceDeviceName = deviceName,
                SourceDisplayName = "Click to Bind",
                SemanticHotspot = semantic.ToString(),
                TargetDeviceId = 1,
                TargetOutput = DevicePresetService.IsWheelOrPedalCategory(category)
                    ? "[Wheel] Button 1"
                    : "[Xbox] Xbox A (Cross / South / Handbrake)"
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

        var leftWell = ControllerHotspotService.LeftStickWell;
        LeftStickOffsetX = Math.Clamp((state.LeftStickX - 0.5) * 2 * leftWell.TravelRadiusPixels, -leftWell.TravelRadiusPixels, leftWell.TravelRadiusPixels);
        LeftStickOffsetY = Math.Clamp((state.LeftStickY - 0.5) * 2 * leftWell.TravelRadiusPixels, -leftWell.TravelRadiusPixels, leftWell.TravelRadiusPixels);

        var rightWell = ControllerHotspotService.RightStickWell;
        RightStickOffsetX = Math.Clamp((state.RightStickX - 0.5) * 2 * rightWell.TravelRadiusPixels, -rightWell.TravelRadiusPixels, rightWell.TravelRadiusPixels);
        RightStickOffsetY = Math.Clamp((state.RightStickY - 0.5) * 2 * rightWell.TravelRadiusPixels, -rightWell.TravelRadiusPixels, rightWell.TravelRadiusPixels);

        var category = DevicePresetService.DetectCategory(state.DeviceName);
        foreach (var hvm in HotspotViewModels)
        {
            int? idx = ControllerHotspotService.GetSemanticButtonIndex(category, hvm.Hotspot.Semantic);
            hvm.IsPressed = idx.HasValue && idx.Value < state.Buttons.Length && state.Buttons[idx.Value];
        }
    }
}

/// <summary>One clickable/highlightable button region on the Visual Binder's controller image.</summary>
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

    public HotspotViewModel(ControllerHotspot hotspot, VisualBinderViewModel parent)
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
