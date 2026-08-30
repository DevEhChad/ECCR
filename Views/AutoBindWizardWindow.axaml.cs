using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ECCR.Views;

/// <summary>
/// A real modal child window (opened via <c>ShowDialog</c> from
/// <see cref="ECCR.ViewModels.AutoBindWizardViewModel.OpenAutoBindWizard"/>), unlike most of
/// the app's other dialogs which are in-window overlays - see <see cref="MainWindow"/>'s doc
/// comment. Its DataContext is set directly to the <see cref="ECCR.ViewModels.AutoBindWizardViewModel"/>
/// instance, not <c>MainWindowViewModel</c>, so its bindings only see wizard-specific state.
/// </summary>
public partial class AutoBindWizardWindow : Window
{
    public AutoBindWizardWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}