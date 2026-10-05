using Avalonia;
using Avalonia.Controls;

namespace Basalt.Shell.Controls;

/// <summary>
/// Ties a property of a code-built control to a theme resource, live.
///
/// A panel built in code used to copy a colour once (<c>new SolidColorBrush(...)</c>
/// or a lookup at construction): it was right until the theme changed, then
/// stayed in the old theme's colour. This is the code equivalent of
/// <c>{DynamicResource Key}</c> in markup, so the panel follows the theme.
/// </summary>
public static class ThemeBinding
{
    /// <summary>Binds <paramref name="property"/> to the theme resource <paramref name="key"/>.</summary>
    public static T Themed<T>(this T control, AvaloniaProperty property, string key)
        where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }
}
