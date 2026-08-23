using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ECCR.Views;

public partial class AutoBindWizardWindow : Window
{
    public AutoBindWizardWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}