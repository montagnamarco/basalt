using System.Globalization;
using System.Xml.Linq;
using Basalt.Designer.Model;

namespace Basalt.Designer;

/// <summary>
/// Moving and resizing a control by editing its XAML.
/// </summary>
/// <remarks>
/// Which attributes to write depends on the container: inside a Canvas a
/// control is positioned with Canvas.Left and Canvas.Top, and anywhere else
/// its Margin is what moves it. Writing the wrong pair moves nothing and
/// leaves an attribute the layout ignores, which looks like a designer that
/// dropped the edit.
/// </remarks>
public static class DesignerGeometry
{
    /// <summary>How a container positions the controls inside it.</summary>
    public enum Positioning
    {
        /// <summary>Canvas.Left and Canvas.Top, in device-independent pixels.</summary>
        Absolute,

        /// <summary>A Margin pushing the control away from its alignment.</summary>
        Margin,
    }

    /// <summary>How the element's container positions it.</summary>
    public static Positioning PositioningOf(XElement element) =>
        element.Parent?.Name.LocalName == "Canvas" ? Positioning.Absolute : Positioning.Margin;

    /// <summary>
    /// The edits that move an element by the given amount.
    /// </summary>
    /// <remarks>
    /// A delta rather than a destination: the caller knows how far the pointer
    /// travelled, and reading the current position back out of the XAML is the
    /// job of whoever writes it.
    /// </remarks>
    public static IReadOnlyList<IDesignerEdit> Move(XElement element, double dx, double dy)
    {
        if (PositioningOf(element) == Positioning.Absolute)
        {
            return
            [
                SetNumber(element, "Canvas.Left", Number(element, "Canvas.Left") + dx),
                SetNumber(element, "Canvas.Top", Number(element, "Canvas.Top") + dy),
            ];
        }

        var margin = MarginOf(element);

        // Left and top grow, right and bottom shrink by the same amount: the
        // control moves without changing size, which is what dragging means.
        return
        [
            SetText(element, "Margin", Thickness(
                margin.Left + dx, margin.Top + dy,
                margin.Right - dx, margin.Bottom - dy)),
        ];
    }

    /// <summary>
    /// The edits that resize an element, moving its origin when a top or left
    /// handle was the one dragged.
    /// </summary>
    public static IReadOnlyList<IDesignerEdit> Resize(
        XElement element,
        double width, double height,
        double dx = 0, double dy = 0)
    {
        var edits = new List<IDesignerEdit>
        {
            // Rounded: a fractional width in the XAML is noise no one asked
            // for, and half a pixel is not a size anybody meant to set.
            SetNumber(element, "Width", Math.Max(0, Math.Round(width))),
            SetNumber(element, "Height", Math.Max(0, Math.Round(height))),
        };

        if (dx != 0 || dy != 0) edits.AddRange(Move(element, dx, dy));

        return edits;
    }

    /// <summary>The element's margin, as four numbers.</summary>
    public static (double Left, double Top, double Right, double Bottom) MarginOf(
        XElement element)
    {
        var written = element.Attribute("Margin")?.Value;

        if (string.IsNullOrWhiteSpace(written)) return (0, 0, 0, 0);

        var parts = written.Split(',', StringSplitOptions.TrimEntries);

        // XAML lets one number mean all four sides and two mean horizontal
        // then vertical.
        return parts.Length switch
        {
            1 when Parse(parts[0]) is { } all => (all, all, all, all),
            2 => (Parse(parts[0]) ?? 0, Parse(parts[1]) ?? 0,
                  Parse(parts[0]) ?? 0, Parse(parts[1]) ?? 0),
            4 => (Parse(parts[0]) ?? 0, Parse(parts[1]) ?? 0,
                  Parse(parts[2]) ?? 0, Parse(parts[3]) ?? 0),
            _ => (0, 0, 0, 0),
        };
    }

    private static IDesignerEdit SetNumber(XElement element, string name, double value) =>
        new SetAttributeEdit(element, name, Write(value));

    private static IDesignerEdit SetText(XElement element, string name, string value) =>
        new SetAttributeEdit(element, name, value);

    private static double Number(XElement element, string name) =>
        Parse(element.Attribute(name)?.Value) ?? 0;

    /// <summary>
    /// A number as XAML writes it.
    /// </summary>
    /// <remarks>
    /// Invariant culture, always: a comma for the decimal point would be read
    /// as the separator between two values, and "12,5" is a point rather than
    /// a number. Written on a machine set to Italian, read anywhere.
    /// </remarks>
    private static string Write(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Thickness(double left, double top, double right, double bottom) =>
        $"{Write(left)},{Write(top)},{Write(right)},{Write(bottom)}";

    private static double? Parse(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
