using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.ViewModels;

/// <summary>
/// Common base for every view model in the app. Currently just re-exports
/// CommunityToolkit's <see cref="ObservableObject"/> (INotifyPropertyChanged support for
/// the [ObservableProperty]/[RelayCommand] source generators), but gives <see cref="ViewLocator"/>
/// a single type to match against and a place to add shared behavior later.
/// </summary>
public class ViewModelBase : ObservableObject
{
}