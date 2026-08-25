using System.Globalization;

namespace Basalt.QuickBasic.Interpretation;

/// <summary>
/// A value while the program runs.
///
/// QuickBASIC has two kinds that behave differently — a number and a string —
/// and mixing them is an error rather than a conversion. Which numeric type a
/// value has is kept so that an INTEGER stays whole where it should.
/// </summary>
public readonly record struct BasicValue
{
    private BasicValue(double number, string? text, BasicType type)
    {
        Number = number;
        Text = text;
        Type = type;
    }

    public double Number { get; }

    /// <summary>The text, when this is a string; null when it is a number.</summary>
    public string? Text { get; }

    public BasicType Type { get; }

    public bool IsString => Text is not null;

    public static BasicValue Of(double value, BasicType type = BasicType.Double) =>
        new(Round(value, type), null, type);

    public static BasicValue Of(string value) => new(0, value, BasicType.String);

    /// <summary>
    /// The zero of a type.
    ///
    /// QuickBASIC starts every variable at zero or the empty string rather
    /// than leaving it unset, and programs rely on it.
    /// </summary>
    public static BasicValue Default(BasicType type) =>
        type == BasicType.String ? Of("") : Of(0, type);

    /// <summary>
    /// Whether this counts as true.
    ///
    /// Anything other than zero, as QuickBASIC has it: its own comparisons
    /// give -1 for true, and both -1 and 1 have to count.
    /// </summary>
    public bool IsTrue => !IsString && Number != 0;

    /// <summary>True as QuickBASIC writes it, which is -1.</summary>
    public static BasicValue True => Of(-1, BasicType.Integer);

    public static BasicValue False => Of(0, BasicType.Integer);

    public static BasicValue FromBool(bool value) => value ? True : False;

    /// <summary>
    /// The value as the program would print it.
    ///
    /// A number gets a leading space when positive and a trailing one always,
    /// which is what QuickBASIC does and what makes its output line up. Taken
    /// from the compiler's own qb_print_number rather than guessed: the same
    /// program has to print the same thing interpreted and compiled.
    /// </summary>
    public string Display()
    {
        if (Text is { } text) return text;

        var written = Written();

        return Number >= 0 ? $" {written} " : $"{written} ";
    }

    /// <summary>The value as text, without the printing space.</summary>
    public string Written()
    {
        if (Text is { } text) return text;

        if (Number == Math.Floor(Number) && Math.Abs(Number) < 1e15)
            return ((long)Number).ToString(CultureInfo.InvariantCulture);

        // G6 rather than G7: C's %g, which the compiler prints with, keeps six
        // significant digits, and the same program has to print the same thing
        // whichever way it was run. Checked against clang for 3.14159265,
        // 1/3 and 123456.789, where G7 disagrees and G6 does not.
        return Number.ToString("G6", CultureInfo.InvariantCulture)
            .Replace("E", "e", StringComparison.Ordinal);
    }

    /// <summary>The name of the type, for the variables panel.</summary>
    public string TypeName() => Type switch
    {
        BasicType.Integer => "INTEGER",
        BasicType.Long => "LONG",
        BasicType.Single => "SINGLE",
        BasicType.Double => "DOUBLE",
        BasicType.String => "STRING",
        _ => "?"
    };

    /// <summary>
    /// Keeps a value inside what its type can hold.
    ///
    /// An INTEGER is whole and wraps at 32767, as it does in QuickBASIC; a
    /// SINGLE keeps about seven digits.
    /// </summary>
    private static double Round(double value, BasicType type) => type switch
    {
        BasicType.Integer => Wrap(Math.Round(value, MidpointRounding.ToEven), 65536, 32767),
        BasicType.Long => Wrap(Math.Round(value, MidpointRounding.ToEven), 4294967296, 2147483647),
        BasicType.Single => (float)value,
        _ => value
    };

    /// <summary>Wraps a whole number into the range its type holds.</summary>
    private static double Wrap(double value, double range, double highest)
    {
        if (value >= -highest - 1 && value <= highest) return value;

        var wrapped = value % range;

        if (wrapped > highest) wrapped -= range;
        if (wrapped < -highest - 1) wrapped += range;

        return wrapped;
    }
}
