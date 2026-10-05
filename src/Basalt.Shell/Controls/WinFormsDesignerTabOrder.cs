using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FormsControl = System.Windows.Forms.Control;

namespace Basalt.Shell.Controls;

public sealed record WinFormsTabIndexEdit(FormsControl Control, int Before, int After);

/// <summary>Orders siblings by TabIndex and paints the editable order above the Forms preview.</summary>
internal static class WinFormsDesignerTabOrder
{
    internal const double BadgeWidth = 28;
    internal const double BadgeHeight = 18;

    internal static string ParentPath(FormsControl parent)
    {
        var indices = new Stack<int>();
        for (var current = parent; current.Parent is { } ancestor; current = ancestor)
            indices.Push(ancestor.Controls.IndexOf(current));
        return string.Join("/", indices);
    }

    internal static IEnumerable<FormsControl> Descendants(FormsControl parent)
    {
        foreach (FormsControl child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    internal static FormsControl? HitBadge(FormsControl root, Point at, Func<FormsControl, Rect> boundsOnHost)
    {
        foreach (var control in Descendants(root).Reverse())
        {
            if (!control.DesiredVisibility) continue;
            var box = boundsOnHost(control);
            if (new Rect(box.X + 2, box.Y + 2, BadgeWidth, BadgeHeight).Contains(at)) return control;
        }
        return null;
    }

    internal static IReadOnlyList<WinFormsTabIndexEdit> MoveToSlot(FormsControl control, int slot)
    {
        if (control.Parent is null) return [];

        var siblings = control.Parent.Controls.Cast<FormsControl>()
            .Select((sibling, index) => (Sibling: sibling, Index: index))
            .OrderBy(item => item.Sibling.TabIndex)
            .ThenBy(item => item.Index)
            .Select(item => item.Sibling)
            .ToList();
        siblings.Remove(control);
        siblings.Insert(Math.Clamp(slot, 0, siblings.Count), control);

        return siblings.Select((sibling, index) => new WinFormsTabIndexEdit(sibling, sibling.TabIndex, index))
            .Where(edit => edit.Before != edit.After)
            .ToArray();
    }

    internal static Border CreateBadge(int tabIndex, Rect bounds)
    {
        var badge = new Border
        {
            Width = BadgeWidth,
            Height = BadgeHeight,
            CornerRadius = new CornerRadius(3),
            Background = Brushes.MidnightBlue,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = tabIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Foreground = Brushes.White,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(badge, bounds.X + 2);
        Canvas.SetTop(badge, bounds.Y + 2);
        return badge;
    }
}
