using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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

    /// <summary>
    /// Binds <paramref name="property"/> to a brush made from the colour
    /// resource <paramref name="colourKey"/> at <paramref name="opacity"/>, live.
    /// </summary>
    /// <remarks>
    /// For washes: the added and removed lines of a diff, the card behind a chat
    /// message. They used to be a fixed colour at 8 to 15 percent, which is a
    /// tint on white and a smudge on a dark surface; deriving them from the
    /// theme's own success, error or foreground colour keeps the hue meaningful
    /// in every theme. Fading the brush rather than the control leaves the text
    /// on top at full strength.
    /// </remarks>
    public static T ThemedTint<T>(this T control, AvaloniaProperty property, string colourKey, double opacity)
        where T : Control
    {
        control.Bind(property, new TintObservable(control.GetResourceObservable(colourKey), opacity));
        return control;
    }

    private sealed class TintObservable(IObservable<object?> source, double opacity) : IObservable<IBrush?>
    {
        public IDisposable Subscribe(IObserver<IBrush?> observer) =>
            source.Subscribe(new Tint(observer, opacity));

        private sealed class Tint(IObserver<IBrush?> observer, double opacity) : IObserver<object?>
        {
            public void OnNext(object? value) =>
                observer.OnNext(value is Color colour ? new SolidColorBrush(colour, opacity) : null);

            public void OnError(Exception error) => observer.OnError(error);
            public void OnCompleted() => observer.OnCompleted();
        }
    }
}
