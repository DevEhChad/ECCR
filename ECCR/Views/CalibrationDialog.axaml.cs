using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ECCR.Views;

public partial class CalibrationDialog : Window
{
    public CalibrationDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}