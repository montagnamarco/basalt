namespace Basalt.Workspace.Immediate;

/// <summary>What a line typed in the Immediate window asks for.</summary>
public enum ImmediateKind
{
    /// <summary>Nothing to do: a blank line, or a lone "?".</summary>
    Empty,

    /// <summary>Print a value: "?expr" or "Print expr".</summary>
    Print,

    /// <summary>Run for its effect: anything else.</summary>
    Statement
}

/// <summary>
/// A line from the Immediate window, split into what it asks for and the
/// Visual Basic text it asks it of.
///
/// Only the two spellings Visual Basic 6 had for printing are recognised
/// here; whether what follows is valid is for the compiler or the debugger
/// to say, not for a second opinion in this class.
/// </summary>
public sealed record ImmediateInput(ImmediateKind Kind, string Text)
{
    public static ImmediateInput Parse(string? line)
    {
        var trimmed = (line ?? "").Trim();

        if (trimmed.Length == 0) return new ImmediateInput(ImmediateKind.Empty, "");

        if (trimmed[0] == '?') return Printing(trimmed[1..]);

        const string print = "Print";

        // "Print" must stand alone as a word: "PrintCount = 3" assigns.
        if (trimmed.StartsWith(print, StringComparison.OrdinalIgnoreCase)
            && trimmed.Length > print.Length
            && (char.IsWhiteSpace(trimmed[print.Length]) || trimmed[print.Length] == '('))
        {
            return Printing(trimmed[print.Length..]);
        }

        return new ImmediateInput(ImmediateKind.Statement, trimmed);
    }

    private static ImmediateInput Printing(string expression)
    {
        var text = expression.Trim();

        return text.Length == 0
            ? new ImmediateInput(ImmediateKind.Empty, "")
            : new ImmediateInput(ImmediateKind.Print, text);
    }
}
