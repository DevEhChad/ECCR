using Avalonia.Controls;

namespace ECCR.Views;

/// <summary>
/// The Auto-Map wizard as a sidebar page (formerly <c>AutoMapWizardWindow</c>, a separate
/// modal <c>Window</c>). Its DataContext is set directly to
/// <see cref="ECCR.ViewModels.AutoMapWizardViewModel"/>, not <c>MainWindowViewModel</c>.
/// </summary>
public partial class AutoMapWizardView : UserControl
{
    public AutoMapWizardView()
    {
        InitializeComponent();
    }
}
