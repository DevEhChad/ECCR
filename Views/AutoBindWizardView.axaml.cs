using Avalonia.Controls;

namespace ECCR.Views;

/// <summary>
/// The Auto-Bind wizard as a sidebar page (formerly <c>AutoBindWizardWindow</c>, a separate
/// modal <c>Window</c>). Its DataContext is set directly to
/// <see cref="ECCR.ViewModels.AutoBindWizardViewModel"/>, not <c>MainWindowViewModel</c>.
/// </summary>
public partial class AutoBindWizardView : UserControl
{
    public AutoBindWizardView()
    {
        InitializeComponent();
    }
}
