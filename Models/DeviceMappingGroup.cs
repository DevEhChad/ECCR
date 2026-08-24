using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

public partial class DeviceMappingGroup : ObservableObject
{
    public string DeviceName { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<MappingEntry> Entries { get; } = new();

    public int EntryCount => Entries.Count;

    public DeviceMappingGroup(string deviceName)
    {
        DeviceName = deviceName;
        Entries.CollectionChanged += (s, e) => OnPropertyChanged(nameof(EntryCount));
    }
}