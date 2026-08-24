using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ECCR.Views;

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