using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>
/// A panel with its name and icon above it.
///
/// Dock's Tool carries a title and nothing else — there is no icon in its
/// model and none in its controls — so a panel that wants one draws it
/// itself. Without this every panel looked alike until its title was read.
/// </summary>
public sealed class PanelHeader : UserControl
{
    public PanelHeader(string title, IconKind icon, Control content, Control? tools = null)
    {
        Title = title;
        Icon = icon;
        Panel = content;

        // No title: Dock's own tab already carries it, and repeating it read
        // as "Solution Explorer" twice, one above the other.

        var row = new DockPanel { Margin = new Thickness(8, 4, 4, 4) };

        if (tools is not null)
        {
            DockPanel.SetDock(tools, Avalonia.Controls.Dock.Right);
            row.Children.Add(tools);
        }

        row.Children.Add(new IconView
        {
            Kind = icon,
            IconSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        });

        var header = new Border
        {
            Child = row,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80))
        };

        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);

        Content = new DockPanel { Children = { header, content } };
    }

    public string Title { get; }

    public IconKind Icon { get; }

    /// <summary>What the header is above.</summary>
    public Control Panel { get; }
}
