using CommunityToolkit.Mvvm.ComponentModel;
using ECCR.Models;

namespace ECCR.Services;

public partial class PresetBindingItem : ObservableObject
{
    [ObservableProperty]
    private string _physicalName = string.Empty;

    [ObservableProperty]
    private InputType _type = InputType.Button;

    [ObservableProperty]
    private int _physicalIndex;

    [ObservableProperty]
    private string _defaultTargetOutput = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;
}