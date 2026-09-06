using Avalonia.Controls;
using ECCR.ViewModels;

namespace ECCR.Views;

/// <summary>
/// The Games page (game library, formerly part of the combined Profiles page). Mirrors
/// <see cref="HidHideView"/>'s storage-provider wiring pattern since this page's manual-add/
/// banner/exe-path pickers need the hosting TopLevel's <c>StorageProvider</c>.
/// </summary>
public partial class GamesPageView : UserControl
{
    public GamesPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachStorageProviderResolver();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachStorageProviderResolver();
    }

    private void AttachStorageProviderResolver()
    {
        if (DataContext is GamesPageViewModel vm)
        {
            vm.StorageProviderResolver = () => TopLevel.GetTopLevel(this)?.StorageProvider;
        }
    }
}
