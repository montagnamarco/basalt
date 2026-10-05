using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Basalt.Shell.Controls;

/// <summary>Dialog actions stay visible while long content scrolls at smaller sizes.</summary>
internal static class WinFormsEditorDialogLayout
{
    public static Control Create(Control body, params Button[] actions)
    {
        var scroll = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        return CreateWithFixedBody(scroll, actions);
    }

    public static Control CreateWithFixedBody(Control body, params Button[] actions)
    {
        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        foreach (var action in actions)
        {
            action.MinWidth = 78;
            action.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(action);
        }

        var layout = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { body, buttons },
        };
        Grid.SetRow(buttons, 1);
        return layout;
    }
}
