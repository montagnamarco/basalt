using FormsControl = System.Windows.Forms.Control;
using Rectangle = System.Drawing.Rectangle;

namespace Basalt.Shell.Controls;

public enum WinFormsLayoutCommand
{
    AlignLeft,
    AlignCenter,
    AlignRight,
    AlignTop,
    AlignMiddle,
    AlignBottom,
    DistributeHorizontal,
    DistributeVertical,
}

/// <summary>Computes one group edit in the selected controls' common parent coordinates.</summary>
internal static class WinFormsDesignerLayout
{
    internal static IReadOnlyList<WinFormsBoundsEdit> Apply(
        IReadOnlyList<FormsControl> selection, WinFormsLayoutCommand command)
    {
        if (selection.Count < 2 || selection.Any(control => !ReferenceEquals(control.Parent, selection[0].Parent)))
            return [];

        var anchor = selection[^1].Bounds;
        var ordered = selection.ToArray();
        var result = new List<WinFormsBoundsEdit>();

        if (command is WinFormsLayoutCommand.DistributeHorizontal or WinFormsLayoutCommand.DistributeVertical)
        {
            if (ordered.Length < 3) return [];
            var horizontal = command == WinFormsLayoutCommand.DistributeHorizontal;
            Array.Sort(ordered, (a, b) => horizontal
                ? a.Left.CompareTo(b.Left) : a.Top.CompareTo(b.Top));
            var first = ordered[0].Bounds;
            var last = ordered[^1].Bounds;
            var occupied = ordered.Skip(1).Take(ordered.Length - 2)
                .Sum(control => horizontal ? control.Width : control.Height);
            var available = horizontal ? last.Left - first.Right : last.Top - first.Bottom;
            var gap = (double)(available - occupied) / (ordered.Length - 1);
            double position = horizontal ? first.Right : first.Bottom;

            for (var i = 1; i < ordered.Length - 1; i++)
            {
                position += gap;
                var control = ordered[i];
                var before = control.Bounds;
                var after = horizontal
                    ? new Rectangle((int)Math.Round(position), before.Y, before.Width, before.Height)
                    : new Rectangle(before.X, (int)Math.Round(position), before.Width, before.Height);
                if (after != before) result.Add(new(control, before, after));
                position += horizontal ? before.Width : before.Height;
            }
            return result;
        }

        foreach (var control in ordered[..^1])
        {
            var before = control.Bounds;
            var x = command switch
            {
                WinFormsLayoutCommand.AlignLeft => anchor.Left,
                WinFormsLayoutCommand.AlignCenter => anchor.Left + (anchor.Width - before.Width) / 2,
                WinFormsLayoutCommand.AlignRight => anchor.Right - before.Width,
                _ => before.X,
            };
            var y = command switch
            {
                WinFormsLayoutCommand.AlignTop => anchor.Top,
                WinFormsLayoutCommand.AlignMiddle => anchor.Top + (anchor.Height - before.Height) / 2,
                WinFormsLayoutCommand.AlignBottom => anchor.Bottom - before.Height,
                _ => before.Y,
            };
            var after = new Rectangle(x, y, before.Width, before.Height);
            if (after != before) result.Add(new(control, before, after));
        }
        return result;
    }
}
