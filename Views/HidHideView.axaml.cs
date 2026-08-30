using System;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ECCR.ViewModels;

namespace ECCR.Views;

public partial class HidHideView : UserControl
{
    public HidHideView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachStorageProviderResolver();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachStorageProviderResolver();
    }

    /// <summary>
    /// The HidHide modal lives inside MainWindow rather than being a window itself, so it
    /// hands the view model a way to reach the hosting TopLevel's storage provider for the
    /// "Add Game" / "Add Folder" pickers.
    /// </summary>
    private void AttachStorageProviderResolver()
    {
        if (DataContext is HidHideViewModel vm)
        {
            vm.StorageProviderResolver = () => TopLevel.GetTopLevel(this)?.StorageProvider;
        }
    }
}
