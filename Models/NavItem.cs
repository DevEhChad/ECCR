using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

/// <summary>
/// One row in the sidebar (see <see cref="ECCR.ViewModels.MainWindowViewModel.PrimaryNavItems"/>/
/// <see cref="ECCR.ViewModels.MainWindowViewModel.UtilityNavItems"/>), or one of Home's
/// quick-action cards, which reuse the same list/command pair rather than a separate
/// navigate-from-card mechanism.
/// </summary>
public partial class NavItem : ObservableObject
{
    public AppPage Page { get; }
    public string Label { get; }
    public string IconGlyph { get; }
    public Bitmap? IconImage { get; }
    public string Description { get; }

    [ObservableProperty]
    private bool _isSelected;

    public NavItem(AppPage page, string label, string iconGlyph, string description = "", Bitmap? iconImage = null)
    {
        Page = page;
        Label = label;
        IconGlyph = iconGlyph;
        IconImage = iconImage;
        Description = description;
    }
}
