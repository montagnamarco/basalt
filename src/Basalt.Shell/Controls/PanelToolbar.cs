using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>
/// The compact row of buttons, toggles and a filter box that sits above a
/// list panel: the shape Visual Studio gives the Error List, Output and Find
/// Results windows.
///
/// Built here once so Problems, Output and Search agree on the row's height,
/// its hairline underneath, and on what a pressed toggle looks like (the
/// <c>panelTool</c> style in Styles.axaml), instead of each panel inventing
/// its own buttons.
/// </summary>
internal static class PanelToolbar
{
    /// <summary>The row: <paramref name="left"/> from the edge, <paramref name="right"/> against the other.</summary>
    internal static Border Row(Control left, Control? right = null)
    {
        // A Grid, not a DockPanel: docking needs the right-hand child added
        // first, and Tab then visits the filter before the toggles that sit to
        // its left. Children in visual order means keyboard order is the
        // visual order.
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        layout.Children.Add(left);
        Grid.SetColumn(left, 0);

        if (right is not null)
        {
            layout.Children.Add(right);
            Grid.SetColumn(right, 1);
        }

        var row = new Border
        {
            Child = layout,
            Padding = new Thickness(Spacing.Tight, Spacing.Hairline * 2),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        row.Themed(Border.BorderBrushProperty, "PanelBorderBrush");
        DockPanel.SetDock(row, Avalonia.Controls.Dock.Top);

        return row;
    }

    /// <summary>A toggle showing an icon and a label, pressed when <paramref name="isChecked"/>.</summary>
    internal static ToggleButton Toggle(IconKind icon, string text, string tip, string? iconBrushKey = null, bool isChecked = true)
    {
        var glyph = new IconView { Kind = icon, IconSize = 14 };
        if (iconBrushKey is not null) glyph.Themed(IconView.ForegroundProperty, iconBrushKey);

        var label = new TextBlock
        {
            Text = text,
            FontSize = PanelMetrics.FontSize,
            VerticalAlignment = VerticalAlignment.Center
        };

        var toggle = Styled(new ToggleButton { IsChecked = isChecked }, tip);
        toggle.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Children = { glyph, label }
        };

        return toggle;
    }

    /// <summary>A toggle whose face is only an icon.</summary>
    internal static ToggleButton IconToggle(IconKind icon, string tip, bool isChecked = false)
    {
        var toggle = Styled(new ToggleButton { IsChecked = isChecked }, tip);
        toggle.Content = new IconView { Kind = icon, IconSize = 14 }
            .Themed(IconView.ForegroundProperty, "ChromeForegroundBrush");

        return toggle;
    }

    /// <summary>A toggle whose face is a short text such as "Aa" or ".*".</summary>
    internal static ToggleButton TextToggle(string text, string tip, bool isChecked = false)
    {
        var toggle = Styled(new ToggleButton { IsChecked = isChecked }, tip);
        toggle.Content = new TextBlock
        {
            Text = text,
            FontSize = PanelMetrics.FontSize,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        return toggle;
    }

    /// <summary>A button whose face is only an icon.</summary>
    internal static Button IconButton(IconKind icon, string tip, Action click)
    {
        var button = Styled(new Button(), tip);
        button.Content = new IconView { Kind = icon, IconSize = 14 }
            .Themed(IconView.ForegroundProperty, "ChromeForegroundBrush");
        button.Click += (_, _) => click();

        return button;
    }

    /// <summary>A one-line text box in the row's height.</summary>
    internal static TextBox Filter(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        FontSize = PanelMetrics.FontSize,
        MinHeight = Spacing.EditorHeight,
        Padding = new Thickness(Spacing.Tight, 2),
        VerticalContentAlignment = VerticalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static T Styled<T>(T button, string tip) where T : TemplatedControl
    {
        button.Classes.Add("panelTool");
        button.Padding = new Thickness(Spacing.Tight + 2, 2);
        button.MinHeight = Spacing.EditorHeight;
        button.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(button, tip);

        return button;
    }
}
