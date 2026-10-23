using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

/// <summary>Adaptive-trigger effect families a DualSense can produce - see <see cref="ECCR.Services.DualSenseTriggerEffect"/> for the report encoding.</summary>
public enum TriggerEffectMode
{
    /// <summary>No effect - the trigger moves freely.</summary>
    Off,
    /// <summary>Constant resistance from <see cref="TriggerEffectSettings.StartPosition"/> to the end of the pull.</summary>
    Resistance,
    /// <summary>Resistance between <see cref="TriggerEffectSettings.WeaponStart"/> and <see cref="TriggerEffectSettings.WeaponEnd"/>, then a sharp release - a gun-trigger click.</summary>
    Weapon,
    /// <summary>Buzzing from <see cref="TriggerEffectSettings.StartPosition"/> onward at <see cref="TriggerEffectSettings.Frequency"/> Hz.</summary>
    Vibration
}

/// <summary>
/// One DualSense adaptive trigger's saved effect, held twice (left/right) by <see cref="GamepadDeviceSettings"/>.
/// Positions run 0 (trigger at rest) to 9 (fully pulled) and strength 1-8, matching what the
/// hardware itself accepts. Weapon uses its own start/end pair because its valid ranges (start 2-7,
/// end 3-8, end after start) differ from the other modes' 0-9 start.
/// </summary>
public partial class TriggerEffectSettings : ObservableObject
{
    [NotifyPropertyChangedFor(nameof(IsResistance), nameof(IsWeapon), nameof(IsVibration), nameof(IsActive), nameof(UsesStartPosition))]
    [ObservableProperty]
    private TriggerEffectMode _mode = TriggerEffectMode.Off;

    /// <summary>0-9: where Resistance/Vibration begin.</summary>
    [ObservableProperty]
    private int _startPosition = 3;

    /// <summary>2-7: where the Weapon effect begins resisting.</summary>
    [ObservableProperty]
    private int _weaponStart = 4;

    /// <summary>3-8: where the Weapon effect releases. Kept above <see cref="WeaponStart"/>.</summary>
    [ObservableProperty]
    private int _weaponEnd = 6;

    /// <summary>1-8.</summary>
    [ObservableProperty]
    private int _strength = 5;

    /// <summary>1-255 Hz, Vibration only.</summary>
    [ObservableProperty]
    private int _frequency = 40;

    [JsonIgnore] public bool IsResistance => Mode == TriggerEffectMode.Resistance;
    [JsonIgnore] public bool IsWeapon => Mode == TriggerEffectMode.Weapon;
    [JsonIgnore] public bool IsVibration => Mode == TriggerEffectMode.Vibration;
    [JsonIgnore] public bool IsActive => Mode != TriggerEffectMode.Off;
    [JsonIgnore] public bool UsesStartPosition => Mode is TriggerEffectMode.Resistance or TriggerEffectMode.Vibration;

    partial void OnWeaponStartChanged(int value)
    {
        if (WeaponEnd <= value) WeaponEnd = value + 1;
    }

    partial void OnWeaponEndChanged(int value)
    {
        if (WeaponStart >= value) WeaponStart = value - 1;
    }
}
