using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

public enum ControllerType
{
    PlayStation4,
    PlayStation5,
    Xbox,
    GenericGamepad
}

public partial class ControllerBindSlot : ObservableObject
{
    [ObservableProperty]
    private string _slotName = string.Empty;

    [ObservableProperty]
    private string _sourceLabel = "Unassigned";

    [ObservableProperty]
    private int _sourceIndex = -1;

    [ObservableProperty]
    private InputType _sourceType = InputType.Button;

    [ObservableProperty]
    private string _xboxTarget = string.Empty;

    [ObservableProperty]
    private string _badgeGlyph = string.Empty;

    [ObservableProperty]
    private string _badgeColor = "#3E7BFA";

    [ObservableProperty]
    private bool _isHighlighted = false;
}