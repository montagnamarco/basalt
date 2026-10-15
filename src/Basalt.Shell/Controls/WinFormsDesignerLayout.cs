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

    /// <summary>Gives each selected control the width of the last one selected, as Visual Studio's Make Same Width.</summary>
    SameWidth,
    SameHeight,
    SameSize,

    /// <summary>Centres the selection, as one group, in its parent's client area. One control is enough.</summary>
    CenterHorizontally,
    CenterVertically,
}

/// <summary>Computes one group edit in the selected controls' common parent coordinates.</summary>
internal static class WinFormsDesignerLayout
{
    internal static IReadOnlyList<WinFormsBoundsEdit> Apply(
        IReadOnlyList<FormsControl> selection, WinFormsLayoutCommand command)
    {
        if (command is WinFormsLayoutCommand.CenterHorizontally or WinFormsLayoutCommand.CenterVertically)
            return CenterInParent(selection, horizontal: command == WinFormsLayoutCommand.CenterHorizontally);

        if (selection.Count < 2 || selection.Any(control => !ReferenceEquals(control.Parent, selection[0].Parent)))
            return [];

        var anchor = selection[^1].Bounds;
        var ordered = selection.ToArray();
        var result = new List<WinFormsBoundsEdit>();

        if (command is WinFormsLayoutCommand.SameWidth or WinFormsLayoutCommand.SameHeight or WinFormsLayoutCommand.SameSize)
        {
            foreach (var control in ordered[..^1])
            {
                var before = control.Bounds;
                var after = new Rectangle(before.X, before.Y,
                    command is WinFormsLayoutCommand.SameHeight ? before.Width : anchor.Width,
                    command is WinFormsLayoutCommand.SameWidth ? before.Height : anchor.Height);
                if (after != before) result.Add(new(control, before, after));
            }
            return result;
        }

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

    /// <summary>
    /// Moves the selection as one block so that its bounding box is centred in the parent's client area. The controls
    /// keep their positions relative to each other; a parent that is not the form is centred the same way.
    /// </summary>
    private static IReadOnlyList<WinFormsBoundsEdit> CenterInParent(IReadOnlyList<FormsControl> selection, bool horizontal)
    {
        var controls = selection.Where(control => control.Parent is not null).ToArray();
        if (controls.Length == 0 || controls.Any(control => !ReferenceEquals(control.Parent, controls[0].Parent)))
            return [];

        var client = controls[0].Parent!.ClientSize;
        var group = controls.Select(control => control.Bounds).Aggregate(Rectangle.Union);
        var dx = horizontal ? (client.Width - group.Width) / 2 - group.X : 0;
        var dy = horizontal ? 0 : (client.Height - group.Height) / 2 - group.Y;
        if (dx == 0 && dy == 0) return [];

        var result = new List<WinFormsBoundsEdit>();
        foreach (var control in controls)
        {
            var before = control.Bounds;
            var after = new Rectangle(before.X + dx, before.Y + dy, before.Width, before.Height);
            if (after != before) result.Add(new(control, before, after));
        }
        return result;
    }
}
