using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Basalt.Shell.Controls;

/// <summary>
/// What sits between Dock's title bar and a panel's content: nothing, or one
/// compact row of the panel's actions.
///
/// It used to be a row of its own on every panel, an icon with no text under
/// a title bar that already carried the name and, below that, the panel's own
/// toolbar: three stacked bars before the first line of content. Dock's title
/// bar now says which panel this is and the row is drawn only when the panel
/// has actions to put in it, in the theme's border colour.
/// </summary>
public sealed class PanelHeader : UserControl
{
    public PanelHeader(string title, Control content, Control? tools = null)
    {
        Title = title;
        Panel = content;
        Tools = tools;

        // No actions, no row: the content goes straight under the title bar.
        if (tools is null)
        {
            Content = content;
            return;
        }

        tools.HorizontalAlignment = HorizontalAlignment.Right;

        var row = new Border
        {
            Child = tools,
            Padding = new Thickness(Spacing.Tight, Spacing.Hairline),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        row.Themed(Border.BorderBrushProperty, "PanelBorderBrush");
        DockPanel.SetDock(row, Avalonia.Controls.Dock.Top);

        Content = new DockPanel { Children = { row, content } };
    }

    public string Title { get; }

    /// <summary>What the header is above.</summary>
    public Control Panel { get; }

    /// <summary>The panel's actions, when it has any: the one row this draws.</summary>
    public Control? Tools { get; }
}
