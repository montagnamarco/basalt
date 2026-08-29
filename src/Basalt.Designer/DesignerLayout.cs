using System.Globalization;
using System.Xml.Linq;
using Basalt.Designer.Model;

namespace Basalt.Designer;

/// <summary>
/// Placing a dropped control the way its container actually lays out.
/// </summary>
/// <remarks>
/// The designer used to answer every drop the same way: write a Margin big
/// enough to push the control under the pointer. In a Canvas that is right,
/// and in a Grid it is absolute positioning wearing a Grid's clothes — the
/// control sits in cell 0,0 held in place by a pixel count that means
/// nothing on a screen of another size.
///
/// So the container is asked what it does with its children, and the drop is
/// written in those terms: a cell for a Grid, a place in the order for a
/// StackPanel, an edge for a DockPanel. Only a Canvas gets coordinates,
/// which is what a Canvas is for.
/// </remarks>
public static class DesignerLayout
{
    /// <summary>How a container decides where its children go.</summary>
    public enum LayoutKind
    {
        /// <summary>Coordinates, as a Canvas does.</summary>
        Coordinates,

        /// <summary>Rows and columns, as a Grid does.</summary>
        Cells,

        /// <summary>One after another, as a StackPanel does.</summary>
        Sequence,

        /// <summary>Against an edge, as a DockPanel does.</summary>
        Edges,

        /// <summary>One child that fills it, as a Border or a Window does.</summary>
        Single,
    }

    /// <summary>What the container does with the controls inside it.</summary>
    public static LayoutKind KindOf(XElement? container) =>
        container?.Name.LocalName switch
        {
            "Canvas" => LayoutKind.Coordinates,
            "Grid" => LayoutKind.Cells,
            "StackPanel" or "WrapPanel" => LayoutKind.Sequence,
            "DockPanel" => LayoutKind.Edges,
            _ => LayoutKind.Single,
        };

    /// <summary>
    /// Where a drop lands, written the way the container understands.
    /// </summary>
    /// <remarks>
    /// <paramref name="offset"/> is where the pointer was inside the
    /// container, and <paramref name="size"/> how big the container is: a
    /// cell or an edge can only be worked out from where the point falls
    /// within the whole, not from the point alone.
    ///
    /// Returns nothing where the container places the child on its own. A
    /// StackPanel stacks whatever it is given, and writing a Margin to say
    /// "third" would fight the panel rather than instruct it.
    /// </remarks>
    public static IReadOnlyList<IDesignerEdit> PlaceDrop(
        XElement child, XElement container, Point offset, Size size)
    {
        switch (KindOf(container))
        {
            case LayoutKind.Coordinates:
                return
                [
                    DesignerGeometry.SetNumber(child, "Canvas.Left", Math.Round(offset.X)),
                    DesignerGeometry.SetNumber(child, "Canvas.Top", Math.Round(offset.Y)),
                ];

            case LayoutKind.Cells:
                return PlaceInGrid(child, container, offset, size);

            case LayoutKind.Edges:
                return [DesignerGeometry.SetText(child, "DockPanel.Dock", EdgeFor(offset, size))];

            // A StackPanel puts it after the last one, which is where the
            // insert already put it, and a Border has the one place.
            default:
                return [];
        }
    }

    /// <summary>
    /// Where a drop would land, described so it can be drawn.
    /// </summary>
    /// <remarks>
    /// The surface outlined the whole container, which says which panel will
    /// take the control and nothing about where in it: a Grid of four cells
    /// looked the same wherever the pointer was, and the cell it actually
    /// went into was a surprise every time.
    ///
    /// Reported as a rectangle in the container's own coordinates, plus a
    /// line for the panels where a drop goes *between* two children rather
    /// than inside a region.
    /// </remarks>
    public static DropHint HintFor(
        XElement container, Point offset, Size size, IReadOnlyList<Rect> childBounds)
    {
        switch (KindOf(container))
        {
            case LayoutKind.Cells:
                return new DropHint(CellAt(container, offset, size), null, CellLabel(container, offset, size));

            case LayoutKind.Edges:
                return new DropHint(EdgeRect(offset, size), null, EdgeFor(offset, size));

            // A stack shows a line where the control will be inserted, since
            // it goes between two others rather than into a region.
            case LayoutKind.Sequence:
                return SequenceHint(container, offset, size, childBounds);

            // A Canvas takes it where the pointer is, and a single-child
            // container has one place: the whole of it is the answer.
            default:
                return new DropHint(new Rect(0, 0, size.Width, size.Height), null, null);
        }
    }

    /// <summary>Where a drop lands, in the container's own coordinates.</summary>
    /// <param name="Region">The area the control will occupy, or the whole container.</param>
    /// <param name="Line">Where it will be inserted, for a panel that stacks its children.</param>
    /// <param name="Label">A word for it — the cell, or the edge — where one helps.</param>
    public readonly record struct DropHint(Rect? Region, (Point From, Point To)? Line, string? Label);

    /// <summary>A rectangle, kept free of any interface type.</summary>
    public readonly record struct Rect(double X, double Y, double Width, double Height);

    /// <summary>The cell a point falls in, as a rectangle.</summary>
    private static Rect CellAt(XElement grid, Point offset, Size size)
    {
        var rows = TrackSizes(grid, "RowDefinitions", "Grid.RowDefinitions");
        var columns = TrackSizes(grid, "ColumnDefinitions", "Grid.ColumnDefinitions");

        var (y, height) = Span(offset.Y, size.Height, rows);
        var (x, width) = Span(offset.X, size.Width, columns);

        return new Rect(x, y, width, height);
    }

    /// <summary>Names the cell, so the number is readable rather than counted.</summary>
    private static string? CellLabel(XElement grid, Point offset, Size size)
    {
        var rows = TrackSizes(grid, "RowDefinitions", "Grid.RowDefinitions");
        var columns = TrackSizes(grid, "ColumnDefinitions", "Grid.ColumnDefinitions");

        // One cell is the whole grid, and "0, 0" on it says nothing.
        if (rows.Count <= 1 && columns.Count <= 1) return null;

        var row = IndexAt(offset.Y, size.Height, rows);
        var column = IndexAt(offset.X, size.Width, columns);

        if (rows.Count <= 1) return $"col {column}";
        if (columns.Count <= 1) return $"riga {row}";

        return $"riga {row}, col {column}";
    }

    /// <summary>Where one track starts and how long it is.</summary>
    private static (double Start, double Length) Span(
        double position, double total, IReadOnlyList<double> weights)
    {
        if (weights.Count <= 1 || total <= 0) return (0, total);

        var sum = weights.Sum();
        if (sum <= 0) return (0, total);

        var start = 0.0;

        foreach (var weight in weights)
        {
            var length = weight / sum * total;

            if (position < start + length) return (start, length);

            start += length;
        }

        // Past the end, which the last track owns.
        return (start - weights[^1] / sum * total, weights[^1] / sum * total);
    }

    /// <summary>The strip a docked control would take.</summary>
    private static Rect EdgeRect(Point offset, Size size)
    {
        // A third of the way in, which is about what a docked control takes
        // and enough to read as "against this side".
        var thickness = 0.3;

        return EdgeFor(offset, size) switch
        {
            "Left" => new Rect(0, 0, size.Width * thickness, size.Height),
            "Right" => new Rect(size.Width * (1 - thickness), 0, size.Width * thickness, size.Height),
            "Top" => new Rect(0, 0, size.Width, size.Height * thickness),
            _ => new Rect(0, size.Height * (1 - thickness), size.Width, size.Height * thickness),
        };
    }

    /// <summary>
    /// Where a control lands in a panel that stacks its children.
    /// </summary>
    /// <remarks>
    /// A line between the two children it will separate, drawn across the
    /// panel: in a stack the question is not "where" but "in what order", and
    /// a rectangle cannot say that.
    /// </remarks>
    private static DropHint SequenceHint(
        XElement container, Point offset, Size size, IReadOnlyList<Rect> childBounds)
    {
        var horizontal = container
            .Attribute("Orientation")?.Value
            .Equals("Horizontal", StringComparison.OrdinalIgnoreCase) == true;

        // Nothing in it yet: the line goes at the start, which is where the
        // first control will go.
        if (childBounds.Count == 0)
        {
            return horizontal
                ? new DropHint(null, (new Point(0, 0), new Point(0, size.Height)), null)
                : new DropHint(null, (new Point(0, 0), new Point(size.Width, 0)), null);
        }

        var along = horizontal ? offset.X : offset.Y;

        // After the last child whose middle the pointer has passed.
        var at = 0.0;

        foreach (var child in childBounds)
        {
            var middle = horizontal
                ? child.X + child.Width / 2
                : child.Y + child.Height / 2;

            if (along < middle) break;

            at = horizontal ? child.X + child.Width : child.Y + child.Height;
        }

        // Drawn across the children rather than across the panel: a
        // StackPanel is stretched to its parent by default, so a line the
        // full width of it floats far past the controls it is meant to sit
        // between and reads as unrelated to them.
        if (horizontal)
        {
            var top = childBounds.Min(c => c.Y);
            var bottom = childBounds.Max(c => c.Y + c.Height);

            return new DropHint(null, (new Point(at, top), new Point(at, bottom)), null);
        }

        var left = childBounds.Min(c => c.X);
        var right = childBounds.Max(c => c.X + c.Width);

        return new DropHint(null, (new Point(left, at), new Point(right, at)), null);
    }

    /// <summary>
    /// The cell a point falls in, as Grid.Row and Grid.Column.
    /// </summary>
    /// <remarks>
    /// A Grid with no rows or columns declared is one cell, and saying
    /// "row 0, column 0" on it is noise: the attributes are written only
    /// where there is a choice to record.
    /// </remarks>
    private static IReadOnlyList<IDesignerEdit> PlaceInGrid(
        XElement child, XElement grid, Point offset, Size size)
    {
        var edits = new List<IDesignerEdit>();

        var rows = TrackSizes(grid, "RowDefinitions", "Grid.RowDefinitions");
        var columns = TrackSizes(grid, "ColumnDefinitions", "Grid.ColumnDefinitions");

        if (rows.Count > 1)
            edits.Add(DesignerGeometry.SetNumber(
                child, "Grid.Row", IndexAt(offset.Y, size.Height, rows)));

        if (columns.Count > 1)
            edits.Add(DesignerGeometry.SetNumber(
                child, "Grid.Column", IndexAt(offset.X, size.Width, columns)));

        return edits;
    }

    /// <summary>
    /// Which track a position falls in, by weight.
    /// </summary>
    /// <remarks>
    /// The tracks are measured as the layout would measure them — a "*" takes
    /// what is left, an "Auto" or a number takes what it asks for — because a
    /// drop two thirds of the way across a grid of "Auto,*" belongs to the
    /// second column even though it is the wider one.
    /// </remarks>
    private static int IndexAt(double position, double total, IReadOnlyList<double> weights)
    {
        if (total <= 0 || weights.Count == 0) return 0;

        var sum = weights.Sum();
        if (sum <= 0) return 0;

        var travelled = 0.0;

        for (var i = 0; i < weights.Count; i++)
        {
            travelled += weights[i] / sum * total;

            if (position < travelled) return i;
        }

        return weights.Count - 1;
    }

    /// <summary>
    /// The relative size of each row or column.
    /// </summary>
    /// <remarks>
    /// Both spellings are read: a Grid may declare its tracks as an attribute
    /// ("*,Auto") or as child elements, and a designer that understood only
    /// one of them would drop controls into the wrong cell of a file written
    /// the other way.
    /// </remarks>
    public static IReadOnlyList<double> TrackSizes(
        XElement grid, string attributeName, string elementName)
    {
        if (grid.Attribute(attributeName)?.Value is { Length: > 0 } written)
            return [.. written.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Weight)];

        var declared = grid
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName == elementName);

        if (declared is null) return [];

        return
        [
            .. declared
                .Elements()
                .Select(definition =>
                    Weight(definition.Attribute("Width")?.Value
                           ?? definition.Attribute("Height")?.Value
                           ?? "*"))
        ];
    }

    /// <summary>
    /// How much room one track asks for, as a number to share out by.
    /// </summary>
    /// <remarks>
    /// Not the real measurement, which only the layout can do: enough to tell
    /// the tracks apart when deciding which one a point is over. "Auto" is
    /// treated as one share, the same as a bare "*", because at drop time
    /// there is no content yet to measure.
    /// </remarks>
    private static double Weight(string size)
    {
        var text = size.Trim();

        if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return 1;

        if (text.EndsWith('*'))
        {
            var factor = text[..^1];

            return factor.Length == 0
                ? 1
                : double.TryParse(factor, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? Math.Max(parsed, 0.0001)
                    : 1;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fixedSize)
            ? Math.Max(fixedSize, 0.0001)
            : 1;
    }

    /// <summary>
    /// The edge a point is nearest, for a DockPanel.
    /// </summary>
    /// <remarks>
    /// By which border is closest in proportion rather than in pixels: in a
    /// wide, short panel the top and bottom are always the nearer two if
    /// measured by distance alone, and every drop would dock the same way.
    /// </remarks>
    private static string EdgeFor(Point offset, Size size)
    {
        var left = size.Width > 0 ? offset.X / size.Width : 0.5;
        var top = size.Height > 0 ? offset.Y / size.Height : 0.5;

        var toLeft = left;
        var toRight = 1 - left;
        var toTop = top;
        var toBottom = 1 - top;

        var nearest = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));

        if (nearest == toLeft) return "Left";
        if (nearest == toRight) return "Right";

        return nearest == toTop ? "Top" : "Bottom";
    }

    /// <summary>
    /// Clears the attributes that placed a control in its old container.
    /// </summary>
    /// <remarks>
    /// Every container reads its own: a `Canvas.Left` means nothing in a Grid
    /// and a `Grid.Row` means nothing in a StackPanel. Left behind they are
    /// silent clutter that comes back to life the moment the control is moved
    /// into that kind of panel again — a control that had been through three
    /// panels carried the leftovers of all of them.
    ///
    /// The Margin goes too: it was a position, not a gap the user asked for,
    /// and carrying it into a panel that stacks pushes the control away from
    /// its neighbours for no reason anybody chose.
    /// </remarks>
    public static IReadOnlyList<IDesignerEdit> ClearPositioning(XElement element) =>
    [
        .. new[] { "Canvas.Left", "Canvas.Top", "Grid.Row", "Grid.Column", "DockPanel.Dock", "Margin" }
            .Where(name => element.Attribute(name) is not null)
            .Select(name => (IDesignerEdit)new SetAttributeEdit(element, name, null))
    ];

    /// <summary>Which way a Grid's tracks run.</summary>
    public enum Track
    {
        Row,
        Column,
    }

    private static string AttributeFor(Track track) =>
        track == Track.Row ? "RowDefinitions" : "ColumnDefinitions";

    private static string ElementFor(Track track) =>
        track == Track.Row ? "Grid.RowDefinitions" : "Grid.ColumnDefinitions";

    /// <summary>
    /// The rows or columns of a grid, as they are written.
    /// </summary>
    /// <remarks>
    /// The text rather than the weights: this is what an editor shows and
    /// puts back, and "Auto" has to survive the round trip as "Auto" rather
    /// than as the number it was measured by.
    /// </remarks>
    public static IReadOnlyList<string> TracksOf(XElement grid, Track track)
    {
        if (grid.Attribute(AttributeFor(track))?.Value is { Length: > 0 } written)
            return [.. written.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim())];

        var declared = grid
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName == ElementFor(track));

        if (declared is null) return [];

        return
        [
            .. declared
                .Elements()
                .Select(definition =>
                    definition.Attribute(track == Track.Row ? "Height" : "Width")?.Value ?? "*")
        ];
    }

    /// <summary>
    /// Rewrites the rows or columns of a grid.
    /// </summary>
    /// <remarks>
    /// Always as the attribute, which is the shorter spelling and the one a
    /// person reads at a glance. Where the file used the element form, that
    /// is removed rather than left behind: two declarations of the same
    /// tracks would be a file Avalonia refuses.
    ///
    /// An empty list clears them, leaving the single implicit track a Grid
    /// has when nothing is declared.
    /// </remarks>
    public static IReadOnlyList<IDesignerEdit> SetTracks(
        XElement grid, Track track, IReadOnlyList<string> sizes)
    {
        var edits = new List<IDesignerEdit>
        {
            new SetAttributeEdit(
                grid,
                AttributeFor(track),
                sizes.Count == 0 ? null : string.Join(",", sizes.Select(Clean))),
        };

        if (grid.Elements().FirstOrDefault(e => e.Name.LocalName == ElementFor(track)) is { } declared)
            edits.Add(new RemoveElementEdit(declared));

        return edits;
    }

    /// <summary>
    /// A track size the layout will accept.
    /// </summary>
    /// <remarks>
    /// Anything unreadable becomes "*", which is the sane default and keeps a
    /// typo from producing XAML that will not load.
    /// </remarks>
    private static string Clean(string size)
    {
        var text = size.Trim();

        if (text.Length == 0) return "*";
        if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return "Auto";
        if (text == "*") return "*";

        if (text.EndsWith('*'))
        {
            return double.TryParse(
                text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)
                && factor > 0
                    ? factor.ToString("0.####", CultureInfo.InvariantCulture) + "*"
                    : "*";
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fixedSize)
            && fixedSize >= 0
                ? fixedSize.ToString("0.####", CultureInfo.InvariantCulture)
                : "*";
    }

    /// <summary>
    /// Where a control sits, so a removed track can take it with it.
    /// </summary>
    /// <remarks>
    /// Removing a row leaves everything below it pointing one row too far
    /// down; the ones that were in the removed row have nowhere to go and are
    /// pulled back to the one before it.
    /// </remarks>
    public static IReadOnlyList<IDesignerEdit> AfterRemoving(
        XElement grid, Track track, int removed)
    {
        var attribute = track == Track.Row ? "Grid.Row" : "Grid.Column";
        var edits = new List<IDesignerEdit>();

        foreach (var child in XamlDocument.ControlChildren(grid))
        {
            var written = child.Attribute(attribute)?.Value;

            if (!int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture, out var at))
                continue;

            if (at < removed) continue;

            var moved = Math.Max(0, at - 1);

            edits.Add(new SetAttributeEdit(
                child,
                attribute,
                moved == 0 ? null : moved.ToString(CultureInfo.InvariantCulture)));
        }

        return edits;
    }

    /// <summary>A point on the surface, kept free of any interface type.</summary>
    public readonly record struct Point(double X, double Y);

    /// <summary>A size on the surface, kept free of any interface type.</summary>
    public readonly record struct Size(double Width, double Height);
}
