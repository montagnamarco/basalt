namespace System.Drawing.Printing;

/// <summary>Converts a measurement between the printer units GDI+ defines.</summary>
public sealed class PrinterUnitConvert
{
    // A private constructor, not a static class: GDI+'s own type has one too,
    // which is why no .ctor shows up in the public surface.
    private PrinterUnitConvert()
    {
    }

    public static double Convert(double value, PrinterUnit fromUnit, PrinterUnit toUnit) =>
        value * UnitsPerDisplay(toUnit) / UnitsPerDisplay(fromUnit);

    public static Point Convert(Point value, PrinterUnit fromUnit, PrinterUnit toUnit) => new(
        (int)Math.Round(Convert((double)value.X, fromUnit, toUnit)),
        (int)Math.Round(Convert((double)value.Y, fromUnit, toUnit)));

    public static Margins Convert(Margins value, PrinterUnit fromUnit, PrinterUnit toUnit)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Margins(
            (int)Math.Round(Convert((double)value.Left, fromUnit, toUnit)),
            (int)Math.Round(Convert((double)value.Right, fromUnit, toUnit)),
            (int)Math.Round(Convert((double)value.Top, fromUnit, toUnit)),
            (int)Math.Round(Convert((double)value.Bottom, fromUnit, toUnit)));
    }

    public static Rectangle Convert(Rectangle value, PrinterUnit fromUnit, PrinterUnit toUnit) => new(
        (int)Math.Round(Convert((double)value.X, fromUnit, toUnit)),
        (int)Math.Round(Convert((double)value.Y, fromUnit, toUnit)),
        (int)Math.Round(Convert((double)value.Width, fromUnit, toUnit)),
        (int)Math.Round(Convert((double)value.Height, fromUnit, toUnit)));

    public static Size Convert(Size value, PrinterUnit fromUnit, PrinterUnit toUnit) => new(
        (int)Math.Round(Convert((double)value.Width, fromUnit, toUnit)),
        (int)Math.Round(Convert((double)value.Height, fromUnit, toUnit)));

    public static int Convert(int value, PrinterUnit fromUnit, PrinterUnit toUnit) =>
        (int)Math.Round(Convert((double)value, fromUnit, toUnit));

    /// <summary>
    /// How many of this unit make one hundredth of an inch (PrinterUnit.Display,
    /// GDI+'s own pivot unit): the ratio two units convert through.
    /// </summary>
    private static double UnitsPerDisplay(PrinterUnit unit) => unit switch
    {
        PrinterUnit.Display => 1.0,
        PrinterUnit.ThousandthsOfAnInch => 10.0,
        PrinterUnit.HundredthsOfAMillimeter => 25.4,
        PrinterUnit.TenthsOfAMillimeter => 2.54,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown PrinterUnit."),
    };
}
