using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ECCR.ViewModels;

namespace ECCR;

/// <summary>
/// Registered app-wide in App.axaml (&lt;Application.DataTemplates&gt;) as a single, long-lived
/// instance and as the fallback resolver for any content bound directly to a
/// <see cref="ViewModelBase"/> instance: "ECCR.ViewModels.FooViewModel" resolves to
/// "ECCR.ViewModels.FooView" by name alone, no explicit registry required. Most notably this is
/// what <c>MainWindow.axaml</c>'s page-content <c>ContentControl</c> uses for
/// <c>MainWindowViewModel.CurrentPageViewModel</c>.
///
/// <para>
/// Caches one built <see cref="Control"/> per ViewModel *instance* (reference identity, the
/// default for a plain object key) rather than building a fresh one on every <see cref="Build"/>
/// call. Every page ViewModel in <c>MainWindowViewModel._pageViewModels</c> is a singleton built
/// once at startup, so without this cache, switching <c>CurrentPageViewModel</c> back to an
/// already-visited page tore down and reconstructed that page's entire visual tree from scratch
/// every single time (the actual cause of the nav-click freeze/frame-drop) - Avalonia's
/// <c>ContentPresenter</c> handles being handed back the same <see cref="Control"/> instance it
/// showed before just fine (detach/reattach, not a full rebuild). Bounded growth: the cache only
/// ever grows to the small, fixed number of distinct ViewModel instances this app ever binds
/// through a DataTemplate (in practice, just the page VMs), never per-navigation.
/// </para>
/// </summary>
public class ViewLocator : IDataTemplate
{
    private readonly Dictionary<object, Control> _viewCache = new();

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        if (_viewCache.TryGetValue(data, out var cached))
            return cached;

        var name = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        Control view = type != null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "Not Found: " + name };

        _viewCache[data] = view;
        return view;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}