using System.Globalization;
using Basalt.Extensibility.Interpretation;

namespace Basalt.QuickBasic.Interpretation;

/// <summary>
/// The functions a QuickBASIC program can call without defining them.
///
/// Kept apart from the interpreter so that a dialect adding its own can take
/// these and add to them rather than copying them.
/// </summary>
public static class Library
{
    /// <summary>Whether a name is one of these.</summary>
    public static bool Has(string name) => Names.Contains(name);

    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "LEN", "MID$", "LEFT$", "RIGHT$", "CHR$", "ASC", "VAL", "STR$",
        "ABS", "INT", "RND", "TIMER", "SQR", "SGN", "UCASE$", "LCASE$",
        "LTRIM$", "RTRIM$", "SPACE$", "STRING$", "INSTR", "FIX", "CINT"
    };

    /// <summary>
    /// Calls one.
    ///
    /// The line is passed so an error says where it happened, which is what a
    /// user needs and what a runtime error without one denies them.
    /// </summary>
    public static BasicValue Call(
        string name, IReadOnlyList<BasicValue> arguments, int line, Random random)
    {
        BasicValue Argument(int index) =>
            index < arguments.Count
                ? arguments[index]
                : throw new InterpreterRuntimeException(
                    $"{name} needs more arguments than it was given.", line);

        string Text(int index) => Argument(index).Written();
        double Number(int index) => Argument(index).Number;

        return name.ToUpperInvariant() switch
        {
            "LEN" => BasicValue.Of(Text(0).Length, BasicType.Long),

            // MID$(s, start) takes the rest; MID$(s, start, length) takes some.
            // Counted from one, as QuickBASIC counts.
            "MID$" => BasicValue.Of(Mid(Text(0), (int)Number(1),
                arguments.Count > 2 ? (int)Number(2) : null)),

            "LEFT$" => BasicValue.Of(Left(Text(0), (int)Number(1))),
            "RIGHT$" => BasicValue.Of(Right(Text(0), (int)Number(1))),

            "CHR$" => BasicValue.Of(((char)(int)Number(0)).ToString()),
            "ASC" => BasicValue.Of(Text(0).Length == 0 ? 0 : Text(0)[0], BasicType.Integer),

            "VAL" => BasicValue.Of(ParseNumber(Text(0))),
            "STR$" => BasicValue.Of(Argument(0).Display().TrimEnd()),

            "ABS" => BasicValue.Of(Math.Abs(Number(0))),
            "INT" => BasicValue.Of(Math.Floor(Number(0))),
            "FIX" => BasicValue.Of(Math.Truncate(Number(0))),
            "CINT" => BasicValue.Of(Math.Round(Number(0), MidpointRounding.ToEven),
                BasicType.Integer),

            "SQR" => Number(0) < 0
                ? throw new InterpreterRuntimeException("SQR of a negative number.", line)
                : BasicValue.Of(Math.Sqrt(Number(0))),

            "SGN" => BasicValue.Of(Math.Sign(Number(0)), BasicType.Integer),

            // RND takes an argument in QuickBASIC but ignores it in the common
            // case, and gives a number from zero up to but not including one.
            "RND" => BasicValue.Of(random.NextDouble(), BasicType.Single),

            "TIMER" => BasicValue.Of(
                DateTime.Now.TimeOfDay.TotalSeconds, BasicType.Single),

            "UCASE$" => BasicValue.Of(Text(0).ToUpperInvariant()),
            "LCASE$" => BasicValue.Of(Text(0).ToLowerInvariant()),
            "LTRIM$" => BasicValue.Of(Text(0).TrimStart()),
            "RTRIM$" => BasicValue.Of(Text(0).TrimEnd()),

            "SPACE$" => BasicValue.Of(new string(' ', Math.Max(0, (int)Number(0)))),

            "STRING$" => BasicValue.Of(Repeat(arguments, (int)Number(0))),

            // INSTR(haystack, needle) counts from one, and gives zero when
            // there is nothing to find.
            "INSTR" => BasicValue.Of(
                Text(0).IndexOf(Text(1), StringComparison.Ordinal) + 1, BasicType.Long),

            _ => throw new InterpreterRuntimeException($"{name} is not a known function.", line)
        };
    }

    private static string Mid(string text, int start, int? length)
    {
        if (start < 1 || start > text.Length) return "";

        var from = start - 1;
        var take = length ?? text.Length - from;

        return text.Substring(from, Math.Clamp(take, 0, text.Length - from));
    }

    private static string Left(string text, int count) =>
        text[..Math.Clamp(count, 0, text.Length)];

    private static string Right(string text, int count)
    {
        var take = Math.Clamp(count, 0, text.Length);

        return text[(text.Length - take)..];
    }

    /// <summary>STRING$(n, c) or STRING$(n, code): both spell a repeated character.</summary>
    private static string Repeat(IReadOnlyList<BasicValue> arguments, int count)
    {
        if (arguments.Count < 2 || count <= 0) return "";

        var second = arguments[1];

        var character = second.IsString
            ? (second.Written().Length > 0 ? second.Written()[0] : ' ')
            : (char)(int)second.Number;

        return new string(character, count);
    }

    /// <summary>
    /// VAL, which reads as much of a string as looks like a number.
    ///
    /// Zero when none of it does, rather than an error: that is what
    /// QuickBASIC gives, and programs rely on it.
    /// </summary>
    private static double ParseNumber(string text)
    {
        var trimmed = text.TrimStart();
        var end = 0;

        while (end < trimmed.Length && IsNumberChar(trimmed, end)) end++;

        return double.TryParse(trimmed[..end], NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static bool IsNumberChar(string text, int index)
    {
        var c = text[index];

        if (char.IsDigit(c) || c == '.') return true;

        // A sign counts at the start, or straight after an exponent.
        if (c is '-' or '+')
            return index == 0 || text[index - 1] is 'e' or 'E';

        return c is 'e' or 'E' && index > 0;
    }
}
