using System.Text;
using System.Text.RegularExpressions;

namespace Basalt.Vb6;

/// <summary>
/// Rewrites Visual Basic 6 as Visual Basic .NET.
/// </summary>
/// <remarks>
/// Far less than it sounds. Measured against the compiler rather than assumed:
/// most of Visual Basic 6 is still Visual Basic .NET, and the library it leaned
/// on — Left, Mid, Format, CStr, Val, Dir, Err, On Error — is still in the
/// framework and still works outside Windows. What is left is a short list of
/// spellings the language dropped.
///
/// A rewrite is only made where the meaning is certain. Anything doubtful is
/// left alone and reported, because a translator that quietly guesses produces
/// code that compiles and behaves differently, which is the worst outcome of
/// the three.
/// </remarks>
public static class CodeTranslation
{
    /// <summary>What translating produced, and what it could not do.</summary>
    public sealed record Result(string Code, IReadOnlyList<Note> Notes);

    /// <summary>Something the translation could not carry over on its own.</summary>
    public sealed record Note(int Line, string Construct, string Explanation);

    /// <summary>
    /// The type-suffixed spellings Visual Basic .NET dropped.
    /// </summary>
    /// <remarks>
    /// Trim$ and Trim are the same function; the $ said it returned a String
    /// rather than a Variant, and there are no Variants any more.
    /// </remarks>
    private static readonly string[] Suffixed =
    [
        "Trim", "LTrim", "RTrim", "Left", "Right", "Mid", "UCase", "LCase",
        "Chr", "ChrW", "Str", "Format", "Space", "String", "Hex", "Oct",
        "Error", "Date", "Time", "Command", "CurDir", "Dir", "Environ",
    ];

    /// <summary>
    /// Constructs with no equivalent, which are reported rather than rewritten.
    /// </summary>
    private static readonly (Regex Pattern, string Name, string Why)[] Unsupported =
    [
        (new Regex(@"^\s*GoSub\b", RegexOptions.IgnoreCase),
            "GoSub",
            "Visual Basic .NET has no GoSub. Make the block a Sub of its own and call it."),

        (new Regex(@"\bAs\s+String\s*\*\s*\d+", RegexOptions.IgnoreCase),
            "fixed-length string",
            "A fixed-length string has no equivalent. Use a String, or " +
            "VBFixedString from Microsoft.VisualBasic.Compatibility if the " +
            "length matters to a file layout."),

        (new Regex(@"^\s*DefInt\b|^\s*DefLng\b|^\s*DefStr\b|^\s*DefVar\b",
            RegexOptions.IgnoreCase),
            "Def statement",
            "DefInt and its relatives are gone. Declare each variable with As."),

        (new Regex(@"\bVarPtr\b|\bStrPtr\b|\bObjPtr\b", RegexOptions.IgnoreCase),
            "pointer function",
            "VarPtr, StrPtr and ObjPtr are not available. The addresses they " +
            "returned do not mean the same thing under a garbage collector."),
    ];

    /// <summary>
    /// Translates the code half of a form or module.
    /// </summary>
    /// <param name="code">The Visual Basic 6 as written.</param>
    /// <param name="handlers">
    /// The controls whose events this code handles, so a Sub named for one can
    /// be given the Handles clause Visual Basic .NET needs.
    /// </param>
    public static Result Translate(string code, IReadOnlyCollection<string>? handlers = null)
    {
        var notes = new List<Note>();
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var written = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // The file-level Option statements the .frm carries are dropped:
            // the generated file declares its own in the only place Visual
            // Basic .NET accepts them, and a second one further down is a
            // compile error rather than a duplicate.
            if (Regex.IsMatch(line, @"^\s*Option\s+(Explicit|Strict|Compare|Base)\b",
                    RegexOptions.IgnoreCase))
                continue;

            foreach (var (pattern, name, why) in Unsupported)
                if (pattern.IsMatch(line))
                    notes.Add(new Note(i + 1, name, why));

            written.AppendLine(TranslateLine(line, handlers));
        }

        return new Result(written.ToString().TrimEnd('\n', '\r'), notes);
    }

    private static string TranslateLine(string line, IReadOnlyCollection<string>? handlers)
    {
        var translated = line;

        // Rewritten outside the string literals and comments only. Inside
        // one, nothing is code: a Trim$ in a message is what the program says
        // to whoever is reading the screen, and changing it changes the
        // message.
        translated = OutsideLiterals(translated, code =>
        {
            code = DropTypeSuffixes(code);

            // Set was how Visual Basic 6 told an object assignment from a
            // value one, and there is no difference to tell any more.
            code = Regex.Replace(code, @"^(\s*)Set\s+", "$1", RegexOptions.IgnoreCase);

            // A Variant held anything, which is what Object does.
            code = Regex.Replace(code, @"\bAs\s+Variant\b", "As Object", RegexOptions.IgnoreCase);

            // Integer was 16 bits and Long was 32. Keeping the names would
            // halve the range of every Integer in the program, silently: a
            // loop that ran to 40000 stops working and nothing says why.
            code = Regex.Replace(code, @"\bAs\s+Integer\b", "As Short", RegexOptions.IgnoreCase);
            code = Regex.Replace(code, @"\bAs\s+Long\b", "As Integer", RegexOptions.IgnoreCase);

            return code;
        });

        // These two read the whole line, arguments included, so they run over
        // the literals rather than around them — and they only move brackets.
        translated = ParenthesiseCall(translated, "MsgBox");
        translated = ParenthesiseCall(translated, "Debug.Print");

        return handlers is null ? translated : AddHandles(translated, handlers);
    }

    /// <summary>
    /// Gives an event handler the Handles clause it needs.
    /// </summary>
    /// <remarks>
    /// This is the one that matters. Visual Basic 6 wired an event by the name
    /// of the procedure — cmdOk_Click *was* the handler — and Visual Basic .NET
    /// wants it said out loud. Without the clause the code compiles, runs, and
    /// does nothing at all: no error, and the button simply never responds.
    /// </remarks>
    private static string AddHandles(string line, IReadOnlyCollection<string> handlers)
    {
        var match = Regex.Match(
            line,
            @"^(\s*(?:Private|Public|Friend)?\s*Sub\s+(\w+)_(\w+)\s*\([^)]*\))\s*$",
            RegexOptions.IgnoreCase);

        if (!match.Success) return line;

        var control = match.Groups[2].Value;
        var eventName = match.Groups[3].Value;

        // The form's own events are handled on Me: Form_Load has no control
        // called Form to hang from.
        if (control.Equals("Form", StringComparison.OrdinalIgnoreCase))
            return match.Groups[1].Value + " Handles Me." + eventName;

        // Only for a control the form actually has. A Sub named like a handler
        // for something that is not there is an ordinary method, and giving it
        // a Handles clause would stop the file compiling.
        return handlers.Contains(control, StringComparer.OrdinalIgnoreCase)
            ? match.Groups[1].Value + $" Handles {control}.{eventName}"
            : line;
    }

    /// <summary>Drops the $ from Trim$ and its relatives.</summary>
    private static string DropTypeSuffixes(string line)
    {
        foreach (var name in Suffixed)
            line = Regex.Replace(
                line, $@"\b{name}\$", name, RegexOptions.IgnoreCase);

        return line;
    }

    /// <summary>
    /// Puts brackets round a call written without them.
    /// </summary>
    /// <remarks>
    /// Visual Basic 6 allowed MsgBox "text", vbCritical as a statement.
    /// Visual Basic .NET rejects it outright — "method arguments must be
    /// enclosed in parentheses" — which is the one error a converted project
    /// hits on nearly every form.
    /// </remarks>
    private static string ParenthesiseCall(string line, string name)
    {
        var match = Regex.Match(
            line,
            $@"^(\s*)(?:Call\s+)?{Regex.Escape(name)}\s+(?!\()(.+?)\s*$",
            RegexOptions.IgnoreCase);

        if (!match.Success) return line;

        var arguments = match.Groups[2].Value;

        // Not when it is already an expression being assigned: "x = MsgBox a"
        // is not a statement call.
        if (arguments.StartsWith("=", StringComparison.Ordinal)) return line;

        return $"{match.Groups[1].Value}{name}({arguments})";
    }

    /// <summary>
    /// Applies a rewrite to the code in a line and to nothing else.
    /// </summary>
    /// <remarks>
    /// Every run between the quotes is handed over; what is inside them, and
    /// everything after a comment mark, is copied through untouched. An
    /// earlier version split the line at the comment only, which left the
    /// string literals in the code half — and "use Trim$ here" came out of the
    /// translator as "use Trim here".
    /// </remarks>
    private static string OutsideLiterals(string line, Func<string, string> rewrite)
    {
        var written = new StringBuilder();
        var start = 0;
        var i = 0;

        while (i < line.Length)
        {
            if (line[i] == '\'')
            {
                written.Append(rewrite(line.Substring(start, i - start)));
                written.Append(line.Substring(i));

                return written.ToString();
            }

            if (line[i] == '"')
            {
                written.Append(rewrite(line.Substring(start, i - start)));

                var end = i + 1;

                // A doubled quote is one character of the string, not its end.
                while (end < line.Length)
                {
                    if (line[end] == '"')
                    {
                        if (end + 1 < line.Length && line[end + 1] == '"') end += 2;
                        else break;
                    }
                    else end++;
                }

                end = Math.Min(end + 1, line.Length);

                written.Append(line.Substring(i, end - i));
                start = end;
                i = end;

                continue;
            }

            i++;
        }

        written.Append(rewrite(line.Substring(start)));

        return written.ToString();
    }
}
