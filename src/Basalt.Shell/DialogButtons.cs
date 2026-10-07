using Avalonia.Controls;
using Avalonia.Layout;

namespace Basalt.Shell;

/// <summary>
/// The button row at the foot of a dialog built in code, in the Windows /
/// Visual Studio order <c>NewWindowDialog.axaml</c> states for the dialogs
/// declared in XAML: the primary action leads, Cancel trails (with any
/// secondary action, such as "Don't Save", between them), the row
/// right-aligned.
///
/// Several dialogs built this row by hand and more than once got the order
/// backwards (Cancel before the primary action, which Windows never does).
/// Callers pass their buttons already in the order they must appear, left to
/// right, so building the row cannot silently reverse them again.
/// </summary>
internal static class DialogButtons
{
    public static StackPanel Row(params Button[] buttonsLeftToRight)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        foreach (var button in buttonsLeftToRight) row.Children.Add(button);

        return row;
    }
}
