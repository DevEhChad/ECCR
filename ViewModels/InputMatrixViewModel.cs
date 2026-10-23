using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Device Mappings page's "Input Matrix" tab: a capability-driven diagnostic tile grid (one
/// tile per physical digital button 0..ButtonCount-1, plus 4 POV-hat direction tiles when the
/// device reports one, plus one gauge tile per reported analog axis) - the generic complement to
/// <see cref="DeviceMappingDiagramViewModel"/>'s fixed artwork, which deliberately places no hotspots
/// for face buttons/paddles since those shift with whatever rim/box is attached (see
/// <see cref="WheelHotspotService"/>'s doc comment). Works identically for wheels, gamepads, and
/// button boxes since it only reads capability counts, never a fixed per-category layout. Scoped
/// to whichever device <see cref="DeviceMappingPageViewModel.SelectedDeviceName"/> already picks, not a
/// second selector.
///
/// <para>
/// Button/axis illumination is strictly momentary - a tile reflects exactly the current polled
/// state with no afterglow/decay timer, so what's on screen always matches the physical hardware
/// on the exact frame the release packet arrives. A separate, narrower timer only detects a
/// button that's been continuously held for an unusually long time (<see cref="GhostSuspectThreshold"/>)
/// as a possible stuck/shorted switch - it never adds any visual lingering after release.
/// </para>
///
/// <para>
/// Nicknames and hidden-input state live in the global, profile-independent
/// <see cref="HardwareProfileService"/> (not the per-profile <see cref="Models.WheelDeviceSettings"/>
/// this tab used before), so they survive a game-profile switch/import/reset and a device replug
/// into a different USB port.
/// </para>
/// </summary>
public partial class InputMatrixViewModel : ViewModelBase, INavigationAware
{
    /// <summary>How long a button must be continuously held before it's flagged as a possible stuck/shorted switch.</summary>
    private static readonly TimeSpan GhostSuspectThreshold = TimeSpan.FromSeconds(30);

    private readonly MainWindowViewModel _host;
    private readonly DeviceMappingPageViewModel _parent;
    private readonly DispatcherTimer _ghostCheckTimer;
    private bool _isActive;

    public ObservableCollection<InputMatrixTileViewModel> Tiles { get; } = new();
    public ObservableCollection<InputMatrixAxisTileViewModel> AxisTiles { get; } = new();

    /// <summary>The device the grid reflects - forwarded from the page's shared device list, not owned here.</summary>
    public string? SelectedDeviceName => _parent.SelectedDeviceName;

    public bool HasTiles => Tiles.Count > 0;
    public bool HasAxisTiles => AxisTiles.Count > 0;

    [ObservableProperty] private uint _buttonBitmask;
    [ObservableProperty] private int _povAngleHundredths = -1;

    public string ButtonBitmaskHex => $"0x{ButtonBitmask:X8}";
    public string ButtonBitmaskDecimal => ButtonBitmask.ToString();
    public string PovAngleDisplay => PovAngleHundredths < 0 ? "-1 (Centered)" : $"{PovAngleHundredths / 100.0:0.0}°";

    /// <summary>Set by the view's code-behind so Export/Import can open a file picker without this view model depending on any specific window/control.</summary>
    public Func<IStorageProvider?>? StorageProviderResolver { get; set; }

    partial void OnButtonBitmaskChanged(uint value)
    {
        OnPropertyChanged(nameof(ButtonBitmaskHex));
        OnPropertyChanged(nameof(ButtonBitmaskDecimal));
    }

    partial void OnPovAngleHundredthsChanged(int value) => OnPropertyChanged(nameof(PovAngleDisplay));

    public InputMatrixViewModel(MainWindowViewModel host, DeviceMappingPageViewModel parent)
    {
        _host = host;
        _parent = parent;
        AutoMapWizard = host.CreateAutoMapWizard();

        RebuildTiles();

        _parent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeviceMappingPageViewModel.SelectedDeviceName))
            {
                OnPropertyChanged(nameof(SelectedDeviceName));
                RebuildTiles();
            }
        };

        // Ghost/stuck-input detection only - not a visual decay timer. A 1s interval is plenty
        // coarse for a 30s threshold (versus the old 150ms afterglow timer this replaced), and it
        // only ever flags a tile that's still actively held; release clears the flag instantly.
        // Started/stopped from OnNavigatedTo/OnNavigatedFrom, not here - see INavigationAware.
        _ghostCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ghostCheckTimer.Tick += OnGhostCheckTick;
    }

    /// <summary>Starts the live-state subscription and ghost-check timer - only while the Device Mappings page is actually on screen, see <see cref="INavigationAware"/>.</summary>
    public void OnNavigatedTo()
    {
        if (_isActive) return;
        _isActive = true;
        _host.OnLiveDeviceStateUpdated += HandleLiveDeviceState;
        _ghostCheckTimer.Start();
    }

    public void OnNavigatedFrom()
    {
        if (!_isActive) return;
        _isActive = false;
        _host.OnLiveDeviceStateUpdated -= HandleLiveDeviceState;
        _ghostCheckTimer.Stop();
    }

    /// <summary>Rebuilds <see cref="Tiles"/>/<see cref="AxisTiles"/> from the selected device's reported capabilities, re-reading each tile's saved nickname from the global hardware store.</summary>
    private void RebuildTiles()
    {
        Tiles.Clear();
        AxisTiles.Clear();

        string? deviceName = SelectedDeviceName;
        if (!string.IsNullOrEmpty(deviceName))
        {
            var info = _host.GetDeviceHardwareInfo(deviceName);
            int buttonCount = info?.ButtonCount ?? 0;
            int axisCount = info?.AxisCount ?? 0;
            bool hasPov = info?.HasPov ?? false;
            (int VendorId, int ProductId)? vidPid = info != null ? (info.VendorId, info.ProductId) : null;

            var hwProfile = HardwareProfileService.GetOrCreate(_host, deviceName);

            for (int i = 0; i < buttonCount; i++)
            {
                Tiles.Add(new InputMatrixTileViewModel(InputType.Button, i,
                    DevicePresetService.GetButtonDisplayName(deviceName, i, vidPid),
                    hwProfile.GetButtonNickname(InputType.Button, i), this));
            }

            if (hasPov)
            {
                foreach (int idx in new[] { 128, 129, 130, 131 }) // Up, Right, Down, Left
                {
                    Tiles.Add(new InputMatrixTileViewModel(InputType.Button, idx,
                        DevicePresetService.GetButtonDisplayName(deviceName, idx, vidPid),
                        hwProfile.GetButtonNickname(InputType.Button, idx), this));
                }
            }

            for (int i = 0; i < axisCount; i++)
            {
                AxisTiles.Add(new InputMatrixAxisTileViewModel(i,
                    DevicePresetService.GetAxisDisplayName(deviceName, i, vidPid),
                    DevicePresetService.IsCenteredAxis(deviceName, i, vidPid),
                    hwProfile.GetButtonNickname(InputType.Axis, i), this));
            }
        }

        OnPropertyChanged(nameof(HasTiles));
        OnPropertyChanged(nameof(HasAxisTiles));
    }

    private void HandleLiveDeviceState(LiveDeviceInputState state)
    {
        if (!string.Equals(state.DeviceName, SelectedDeviceName, StringComparison.OrdinalIgnoreCase)) return;

        foreach (var tile in Tiles)
        {
            tile.ApplyLiveState(tile.Index < state.Buttons.Length && state.Buttons[tile.Index]);
        }

        foreach (var axisTile in AxisTiles)
        {
            if (axisTile.Index >= state.RawAxes.Length) continue;
            axisTile.RawValue = state.RawAxes[axisTile.Index];
            axisTile.NormalizedValue = axisTile.Index < state.NormalizedAxes.Length ? state.NormalizedAxes[axisTile.Index] : 0.0;
        }

        PovAngleHundredths = state.PovAngleHundredths;

        uint mask = 0;
        int bitCount = Math.Min(32, state.Buttons.Length);
        for (int i = 0; i < bitCount; i++)
        {
            if (state.Buttons[i]) mask |= 1u << i;
        }
        ButtonBitmask = mask;
    }

    private void OnGhostCheckTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        foreach (var tile in Tiles)
        {
            tile.IsGhostSuspect = tile.IsPressed && tile.PressStartUtc is { } start && now - start > GhostSuspectThreshold;
        }
    }

    /// <summary>Commits (or, given null/blank, clears) one tile's nickname - the Apply/Save and Clear Nickname actions of the tile's editor flyout.</summary>
    internal void CommitNickname(InputMatrixTileViewModel tile, string? nickname)
    {
        string? deviceName = SelectedDeviceName;
        if (string.IsNullOrEmpty(deviceName)) return;

        var hwProfile = HardwareProfileService.GetOrCreate(_host, deviceName);
        hwProfile.SetButtonNickname(tile.Type, tile.Index, nickname);
        HardwareProfileService.Save();
        tile.Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();

        _parent.RefreshNicknameDisplaysForDevice(deviceName);
    }

    /// <summary>Commits (or, given null/blank, clears/"resets to default") one axis tile's alias - the axis card's alias editor flyout.</summary>
    internal void CommitNickname(InputMatrixAxisTileViewModel axisTile, string? nickname)
    {
        string? deviceName = SelectedDeviceName;
        if (string.IsNullOrEmpty(deviceName)) return;

        var hwProfile = HardwareProfileService.GetOrCreate(_host, deviceName);
        hwProfile.SetButtonNickname(InputType.Axis, axisTile.Index, nickname);
        HardwareProfileService.Save();
        axisTile.Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();

        _parent.RefreshNicknameDisplaysForDevice(deviceName);
    }

    /// <summary>Assigns one physical axis directly to a virtual output target - the axis card's quick-assign flyout. The actual mutation lives on <see cref="MainWindowViewModel"/> since it needs its private autosave hook.</summary>
    internal void QuickAssignAxis(int axisIndex, string targetOutput)
    {
        string? deviceName = SelectedDeviceName;
        if (string.IsNullOrEmpty(deviceName)) return;

        _host.QuickAssignAxis(deviceName, axisIndex, targetOutput);
    }

    /// <summary>Backs the Auto-Map flyout: the same layout / player / wheel-slot choices as the Auto Map page, scoped to <see cref="SelectedDeviceName"/>.</summary>
    public AutoMapWizardViewModel AutoMapWizard { get; }

    /// <summary>Points the flyout's wizard at the currently selected device and preselects the layout that suits it - run each time the flyout opens.</summary>
    public void PrepareAutoMap()
    {
        string? deviceName = SelectedDeviceName;
        if (string.IsNullOrEmpty(deviceName)) return;

        AutoMapWizard.WizardSelectedDevice = deviceName;
        AutoMapWizard.PrepareForNavigation();
    }

    /// <summary>Applies the flyout's chosen layout to the selected device - same path as the Auto Map page's Apply Configuration.</summary>
    public void ApplyAutoMap()
    {
        if (string.IsNullOrEmpty(SelectedDeviceName)) return;

        AutoMapWizard.ApplyWizardMappings();
        RebuildTiles();
    }

    [RelayCommand]
    public async Task ExportDeviceConfig()
    {
        string? deviceName = SelectedDeviceName;
        var storageProvider = StorageProviderResolver?.Invoke();
        if (storageProvider == null || string.IsNullOrEmpty(deviceName)) return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Device Config",
            SuggestedFileName = $"{deviceName}-hardware-profile.json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        if (file == null) return;

        var profile = HardwareProfileService.GetOrCreate(_host, deviceName);
        await using var stream = await file.OpenWriteAsync();
        await JsonSerializer.SerializeAsync(stream, profile, new JsonSerializerOptions { WriteIndented = true });
    }

    [RelayCommand]
    public async Task ImportDeviceConfig()
    {
        string? deviceName = SelectedDeviceName;
        var storageProvider = StorageProviderResolver?.Invoke();
        if (storageProvider == null || string.IsNullOrEmpty(deviceName)) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Device Config",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        if (files.Count == 0) return;

        HardwareDeviceProfile? imported;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            imported = await JsonSerializer.DeserializeAsync<HardwareDeviceProfile>(stream);
        }
        catch (JsonException)
        {
            return; // malformed file - silently ignored, same tolerance every other JSON load in this app has
        }
        if (imported == null) return;

        HardwareProfileService.ReplaceEntry(_host, deviceName, imported);
        RebuildTiles();
        _parent.RefreshNicknameDisplaysForDevice(deviceName);
    }
}

/// <summary>
/// One tile in the Input Matrix grid: a single physical button or POV-hat direction, its
/// default display name, an optional user nickname, live press state, and stuck/ghost-input
/// detection - the generic tile counterpart of <see cref="HotspotViewModel"/>/<see cref="WheelHotspotViewModel"/>.
/// </summary>
public partial class InputMatrixTileViewModel : ObservableObject
{
    private readonly InputMatrixViewModel _owner;

    public InputType Type { get; }
    public int Index { get; }
    public string DefaultLabel { get; }

    [ObservableProperty]
    private string? _nickname;

    [ObservableProperty]
    private bool _isPressed;

    /// <summary>True while this tile has been continuously held past <see cref="InputMatrixViewModel"/>'s ghost-suspect threshold - a possible stuck/shorted switch. Cleared the instant the tile is released.</summary>
    [ObservableProperty]
    private bool _isGhostSuspect;

    /// <summary>When the current press started, or null while released - used only for ghost-input detection, not for any visual lingering.</summary>
    public DateTime? PressStartUtc { get; private set; }

    /// <summary>Primary tile text: the nickname if set, else the generic name (e.g. "Wheel Button 4").</summary>
    public string PrimaryLabel => Nickname ?? DefaultLabel;

    /// <summary>Muted secondary text showing the raw name once a nickname is set (e.g. "(Wheel Button 4)") - null (hidden) otherwise.</summary>
    public string? SecondaryLabel => Nickname != null ? $"({DefaultLabel})" : null;

    public bool HasNickname => Nickname != null;

    public InputMatrixTileViewModel(InputType type, int index, string defaultLabel, string? nickname, InputMatrixViewModel owner)
    {
        Type = type;
        Index = index;
        DefaultLabel = defaultLabel;
        _nickname = nickname;
        _owner = owner;
    }

    /// <summary>Strictly momentary: reflects exactly the current polled state, illuminating/reverting on the exact frame the press/release packet arrives - no afterglow.</summary>
    internal void ApplyLiveState(bool pressed)
    {
        if (pressed && !IsPressed) PressStartUtc = DateTime.UtcNow;
        else if (!pressed) { PressStartUtc = null; IsGhostSuspect = false; }

        IsPressed = pressed;
    }

    /// <summary>Commits the current <see cref="Nickname"/> text (or clears it, given null/blank) - the tile editor flyout's Apply/Save and Clear Nickname actions.</summary>
    internal void CommitNickname(string? nickname) => _owner.CommitNickname(this, nickname);

    partial void OnNicknameChanged(string? value)
    {
        OnPropertyChanged(nameof(PrimaryLabel));
        OnPropertyChanged(nameof(SecondaryLabel));
        OnPropertyChanged(nameof(HasNickname));
    }
}

/// <summary>
/// One live analog-axis gauge in the Input Matrix grid: a centered bidirectional bar for
/// steering-like axes (<see cref="IsCentered"/>), a unidirectional 0-100% fill bar otherwise
/// (pedals/sliders) - see <see cref="DevicePresetService.IsCenteredAxis"/> for how that's decided
/// without an assigned <c>TargetOutput</c> to string-match against.
/// </summary>
public partial class InputMatrixAxisTileViewModel : ObservableObject
{
    private readonly InputMatrixViewModel _owner;

    public int Index { get; }
    public string DefaultLabel { get; }
    public bool IsCentered { get; }

    [ObservableProperty] private string? _nickname;

    [ObservableProperty] private int _rawValue;
    [ObservableProperty] private double _normalizedValue;

    /// <summary>-1..1 for a centered axis's bidirectional bar (0 = rest/center); always 0 for a unidirectional axis (not used by its fill bar, which binds <see cref="NormalizedValue"/> directly).</summary>
    public double CenteredOffset => IsCentered ? (NormalizedValue - 0.5) * 2.0 : 0.0;

    public double PercentDisplay => NormalizedValue * 100.0;

    /// <summary>Primary card text: the alias if set, else the generic name (e.g. "Axis Z").</summary>
    public string DisplayName => Nickname ?? DefaultLabel;

    /// <summary>Muted secondary text showing the raw name once an alias is set (e.g. "(Axis Z)") - null (hidden) otherwise.</summary>
    public string? SecondaryLabel => Nickname != null ? $"({DefaultLabel})" : null;

    public bool HasNickname => Nickname != null;

    public InputMatrixAxisTileViewModel(int index, string defaultLabel, bool isCentered, string? nickname, InputMatrixViewModel owner)
    {
        Index = index;
        DefaultLabel = defaultLabel;
        IsCentered = isCentered;
        _nickname = nickname;
        _owner = owner;
    }

    /// <summary>Assigns this axis to a virtual output target - the axis card flyout's target-list selection.</summary>
    internal void AssignTarget(string targetOutput) => _owner.QuickAssignAxis(Index, targetOutput);

    /// <summary>Commits the current <see cref="Nickname"/> text (or clears/"resets to default", given null/blank) - the axis card's alias editor flyout's Apply/Reset actions.</summary>
    internal void CommitNickname(string? nickname) => _owner.CommitNickname(this, nickname);

    partial void OnNormalizedValueChanged(double value)
    {
        OnPropertyChanged(nameof(CenteredOffset));
        OnPropertyChanged(nameof(PercentDisplay));
    }

    partial void OnNicknameChanged(string? value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SecondaryLabel));
        OnPropertyChanged(nameof(HasNickname));
    }
}
