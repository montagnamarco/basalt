using Avalonia;

namespace Basalt.Shell.Controls;

/// <summary>
/// The spacings the interface uses.
///
/// A survey of the panels found fifty distinct paddings and eight gap sizes,
/// including 3, 5, 10 and 14 — values close enough to their neighbours that
/// nobody chose them deliberately, and far enough that panels sitting side by
/// side did not line up.
///
/// The scale is 2, 4, 6, 8, 12, 16, 20, 24: it holds the values that were
/// already the most used and drops the ones in between. Anything on it is a
/// choice; anything off it is a slip, which is why there is a test that says
/// so.
/// </summary>
public static class Spacing
{
    /// <summary>Between things that belong to one another, like a label and its field.</summary>
    public const double Tight = 4;

    /// <summary>The usual gap between controls in a row or a column.</summary>
    public const double Normal = 8;

    /// <summary>Between groups of controls.</summary>
    public const double Loose = 16;

    /// <summary>Inside a row of a list.</summary>
    public static Thickness RowPadding { get; } = new(8, 4);

    /// <summary>
    /// How tall an editor in the property grid is.
    /// </summary>
    /// <remarks>
    /// One height for all of them. Left to themselves a check box, a combo
    /// and a numeric box each pick their own, so a column of properties comes
    /// out ragged and the rows stop reading as a list.
    /// </remarks>
    public const double EditorHeight = 22;

    /// <summary>
    /// How big a check box is drawn in the property grid.
    /// </summary>
    /// <remarks>
    /// Smaller than the platform default, which is sized for a form where a
    /// tick is a decision the user came for. Here it is one row of forty in a
    /// panel, and at its natural size it makes its row taller than the rest.
    /// </remarks>
    public const double CheckBoxSize = 14;

    /// <summary>Inside a panel, around its content.</summary>
    public static Thickness PanelPadding { get; } = new(12);

    /// <summary>Inside a dialog, around everything.</summary>
    public static Thickness DialogPadding { get; } = new(16);

    /// <summary>Inside a toolbar or status bar cell.</summary>
    public static Thickness CellPadding { get; } = new(6, 0);

    /// <summary>Between consecutive lines of text in one block.</summary>
    public const double Hairline = 1;

    /// <summary>
    /// Every value the scale allows.
    ///
    /// The zero is there because no padding at all is a deliberate choice
    /// rather than a slip, and the one because consecutive lines of text in a
    /// single block are meant to sit almost touching.
    /// </summary>
    /// <summary>
    /// The gap between two rows that belong together — two check boxes about
    /// the same thing, say.
    /// </summary>
    public const double Small = 6;

    /// <summary>
    /// The gap between two rows that do not: a labelled field needs room so
    /// its label reads as belonging to the field below it.
    /// </summary>
    public const double Large = 16;

    public static IReadOnlyList<double> Scale { get; } = [0, 1, 2, 4, 6, 8, 12, 16, 20, 24];

    /// <summary>Whether a number is on the scale.</summary>
    public static bool IsOnScale(double value) => Scale.Contains(value);

    /// <summary>Whether every side of a thickness is on the scale.</summary>
    public static bool IsOnScale(Thickness thickness) =>
        IsOnScale(thickness.Left) && IsOnScale(thickness.Top)
        && IsOnScale(thickness.Right) && IsOnScale(thickness.Bottom);
}
