using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ECCR.ViewModels;

namespace ECCR;

/// <summary>
/// Registered app-wide in App.axaml (&lt;Application.DataTemplates&gt;) as the fallback
/// resolver for any content bound directly to a <see cref="ViewModelBase"/> instance:
/// "ECCR.ViewModels.FooViewModel" resolves to "ECCR.ViewModels.FooView" by name alone, no
/// explicit registry required. In practice every dialog in this app (Calibration, Auto-Bind
/// Wizard, HidHide) is instantiated explicitly in code-behind instead, so this locator is
/// mostly latent - it only kicks in if a future view binds a ViewModelBase as raw content.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var name = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}