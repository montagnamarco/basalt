namespace Basalt.Designer;

/// <summary>How several selected controls are lined up with one another.</summary>
public enum AlignmentCommand
{
    Left,
    HorizontalCentre,
    Right,
    Top,
    VerticalCentre,
    Bottom,
    SameWidth,
    SameHeight,
}

/// <summary>
/// Working out where each control goes when several are aligned.
/// </summary>
/// <remarks>
/// Separate from the surface so it can be tested without a window: the
/// arithmetic is where alignment goes wrong, and driving a window to check it
/// tests the window instead.
/// </remarks>
public static class AlignmentArithmetic
{
    /// <summary>Where a control is and how big it is.</summary>
    public readonly record struct Box(double X, double Y, double Width, double Height);

    /// <summary>How far a control moves, and what size it takes.</summary>
    public readonly record struct Adjustment(double Dx, double Dy, double Width, double Height);

    /// <summary>
    /// What each control must do to line up with the last one selected.
    /// </summary>
    /// <param name="boxes">
    /// The selected controls, in the order they were picked. The last is the
    /// one the others align to, as in Visual Studio: the anchor is whichever
    /// the user picked most recently, which is the one they were looking at.
    /// </param>
    public static IReadOnlyList<Adjustment> Align(
        IReadOnlyList<Box> boxes, AlignmentCommand command)
    {
        if (boxes.Count < 2) return [];

        var anchor = boxes[^1];

        return [.. boxes.Select(box => command switch
        {
            AlignmentCommand.Left => Move(anchor.X - box.X, 0, box),
            AlignmentCommand.Right =>
                Move(anchor.X + anchor.Width - (box.X + box.Width), 0, box),
            AlignmentCommand.HorizontalCentre =>
                Move(anchor.X + anchor.Width / 2 - (box.X + box.Width / 2), 0, box),

            AlignmentCommand.Top => Move(0, anchor.Y - box.Y, box),
            AlignmentCommand.Bottom =>
                Move(0, anchor.Y + anchor.Height - (box.Y + box.Height), box),
            AlignmentCommand.VerticalCentre =>
                Move(0, anchor.Y + anchor.Height / 2 - (box.Y + box.Height / 2), box),

            AlignmentCommand.SameWidth => new Adjustment(0, 0, anchor.Width, box.Height),
            _ => new Adjustment(0, 0, box.Width, anchor.Height),
        })];
    }

    /// <summary>Whether a command changes size rather than position.</summary>
    public static bool IsResize(AlignmentCommand command) =>
        command is AlignmentCommand.SameWidth or AlignmentCommand.SameHeight;

    private static Adjustment Move(double dx, double dy, Box box) =>
        new(dx, dy, box.Width, box.Height);
}
