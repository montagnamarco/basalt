using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Basalt.Shell.Controls;

namespace Basalt.Shell.Database;

/// <summary>
/// The buttons of the database views: an icon and the label, with a tooltip, as the Problems, Output and
/// Search panels draw theirs (see <see cref="PanelToolbar.Toggle"/>).
/// </summary>
/// <remarks>
/// The button's <c>Content</c> stays the label, a string, and the icon comes from its content template. A test
/// or a screen reader that asks a button for its content still gets the words, and the icon is a drawing that
/// follows the theme: an icon with a colour of its own keeps it, the others take the chrome foreground, so none
/// is drawn grey-on-grey on a dark panel.
/// </remarks>
internal static class DatabaseButtons
{
    /// <summary>A button showing <paramref name="icon"/> and <paramref name="text"/>. The tooltip defaults to the text.</summary>
    public static Button Create(IconKind icon, string text, string? tip = null, double fontSize = 0)
    {
        var size = fontSize > 0 ? fontSize : PanelMetrics.FontSize;

        var button = new Button
        {
            Content = text,
            FontSize = size,
            ContentTemplate = new FuncDataTemplate<string>((label, _) => Face(icon, label, size), supportsRecycling: false),
        };

        ToolTip.SetTip(button, tip ?? text);

        return button;
    }

    /// <summary>How much of its strength an icon keeps on a disabled button; the label's own disabled colour is about as faint.</summary>
    internal const double DisabledOpacity = 0.4;

    private static StackPanel Face(IconKind icon, string? label, double size)
    {
        var glyph = new IconView { Kind = icon, IconSize = 14, VerticalAlignment = VerticalAlignment.Center };

        // A neutral icon follows the chrome; one with a meaning of its own (Run green, Stop red) keeps it.
        if (IdeIcons.AccentFor(icon) is null) glyph.Themed(IconView.ForegroundProperty, "ChromeForegroundBrush");

        // The icon has a brush of its own, which the button's disabled state does not reach, so it would stay at
        // full strength beside greyed text. It is dimmed with the button, whichever way the button is disabled.
        void Dim() => glyph.Opacity = glyph.IsEffectivelyEnabled ? 1 : DisabledOpacity;

        glyph.AttachedToVisualTree += (_, _) => Dim();
        glyph.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsEffectivelyEnabledProperty) Dim();
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Children =
            {
                glyph,
                new TextBlock { Text = label, FontSize = size, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }
}
