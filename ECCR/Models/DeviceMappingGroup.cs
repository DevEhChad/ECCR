using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

public partial class DeviceMappingGroup : ObservableObject
{
    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<MappingEntry> Entries { get; } = new();

    public int EntryCount => Entries.Count;

    public DeviceMappingGroup(string deviceName)
    {
        DeviceName = deviceName;
        Entries.CollectionChanged += OnEntriesCollectionChanged;
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(EntryCount));

        if (e.NewItems != null)
        {
            foreach (MappingEntry item in e.NewItems)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (MappingEntry item in e.OldItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-notify if any row visual updates occur
        if (e.PropertyName == nameof(MappingEntry.SourceDisplayName) ||
            e.PropertyName == nameof(MappingEntry.TargetOutput))
        {
            OnPropertyChanged(nameof(EntryCount));
        }
    }
}