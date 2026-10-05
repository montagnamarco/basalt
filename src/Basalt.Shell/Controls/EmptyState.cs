using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>
/// What a panel shows when there is nothing to list: an icon and one
/// sentence, centred, in the theme's secondary text colour.
///
/// Eight panels carried their own copy of a faded <c>TextBlock</c>, each with
/// its own margin (12,8,8,8 in one, 8,8 in the next) and a fixed opacity that
/// read as grey on white and as nothing on a dark surface. One control means
/// they agree and follow the theme.
/// </summary>
public sealed class EmptyState : Border
{
    private readonly TextBlock _message;

    public EmptyState(string message, IconKind icon = IconKind.Information)
    {
        _message = new TextBlock
        {
            Text = message,
            FontSize = PanelMetrics.FontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Center
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = Spacing.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (icon != IconKind.None)
        {
            stack.Children.Add(new IconView
            {
                Kind = icon,
                IconSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center
            }.Themed(IconView.ForegroundProperty, "DescriptionForegroundBrush"));
        }

        stack.Children.Add(_message);

        Padding = new Thickness(Spacing.Normal * 2, Spacing.Loose);
        Child = stack;
    }

    /// <summary>The sentence shown, for the tests and for panels that change it.</summary>
    public string Message
    {
        get => _message.Text ?? "";
        set => _message.Text = value;
    }
}
