using Basalt.Extensibility;

namespace Basalt.QuickBasic;

/// <summary>
/// What QuickBASIC can say about a name.
///
/// The proof that the description model is not a Roslyn idea wearing a
/// disguise: QuickBASIC has no compiler, no semantic model and no
/// documentation comments — a table of intrinsics and a parse tree — and it
/// fills the same shape. The tooltip that draws it does not know which
/// language it came from.
/// </summary>
public sealed class QuickBasicSymbolDescriptionProvider : ISymbolDescriptionProvider
{
    /// <summary>
    /// The built-in functions, with what each one takes.
    ///
    /// Written out because there is nowhere to read it from: QuickBASIC's
    /// intrinsics are part of the language, not of any library the IDE can
    /// open.
    /// </summary>
    private static readonly Dictionary<string, Intrinsic> Intrinsics =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["LEFT$"] = new("LEFT$", "STRING",
                [new("text", "STRING") { Documentation = "The string to take from." },
                 new("count", "INTEGER") { Documentation = "How many characters." }],
                "The first characters of a string."),

            ["RIGHT$"] = new("RIGHT$", "STRING",
                [new("text", "STRING") { Documentation = "The string to take from." },
                 new("count", "INTEGER") { Documentation = "How many characters." }],
                "The last characters of a string."),

            ["MID$"] = new("MID$", "STRING",
                [new("text", "STRING") { Documentation = "The string to take from." },
                 new("start", "INTEGER") { Documentation = "Where to start, counting from 1." },
                 new("count", "INTEGER") { Documentation = "How many characters. Omit for the rest." }],
                "A run of characters from the middle of a string."),

            ["LEN"] = new("LEN", "INTEGER",
                [new("text", "STRING") { Documentation = "The string to measure." }],
                "How many characters a string holds."),

            ["CHR$"] = new("CHR$", "STRING",
                [new("code", "INTEGER") { Documentation = "The character code." }],
                "The character with a given code."),

            ["ASC"] = new("ASC", "INTEGER",
                [new("text", "STRING") { Documentation = "The string to read." }],
                "The code of the first character."),

            ["INSTR"] = new("INSTR", "INTEGER",
                [new("start", "INTEGER") { Documentation = "Where to begin. Omit to start at 1." },
                 new("text", "STRING") { Documentation = "The string to search." },
                 new("find", "STRING") { Documentation = "What to look for." }],
                "Where one string appears inside another, or 0."),

            ["VAL"] = new("VAL", "SINGLE",
                [new("text", "STRING") { Documentation = "The string to read a number from." }],
                "The number at the start of a string."),

            ["STR$"] = new("STR$", "STRING",
                [new("number", "SINGLE") { Documentation = "The number to write out." }],
                "A number as a string."),

            ["ABS"] = new("ABS", "SINGLE",
                [new("number", "SINGLE") { Documentation = "The number." }],
                "A number without its sign."),

            ["INT"] = new("INT", "INTEGER",
                [new("number", "SINGLE") { Documentation = "The number." }],
                "The largest whole number no greater than this one."),

            ["RND"] = new("RND", "SINGLE", [], "A random number between 0 and 1."),

            ["UCASE$"] = new("UCASE$", "STRING",
                [new("text", "STRING") { Documentation = "The string to raise." }],
                "A string in capitals."),

            ["LCASE$"] = new("LCASE$", "STRING",
                [new("text", "STRING") { Documentation = "The string to lower." }],
                "A string in lower case."),

            ["SPACE$"] = new("SPACE$", "STRING",
                [new("count", "INTEGER") { Documentation = "How many spaces." }],
                "A string of spaces."),

            ["TIMER"] = new("TIMER", "SINGLE", [],
                "Seconds since midnight.")
        };

    public Task<SymbolDescription?> DescribeAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var name = WordAt(document.Text, position);

        if (name.Length == 0) return Task.FromResult<SymbolDescription?>(null);

        return Task.FromResult(Describe(document.Text, name, activeParameter: -1));
    }

    public Task<SymbolDescriptionSet?> DescribeCallAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var call = CallAt(document.Text, position);

        if (call is not { } found) return Task.FromResult<SymbolDescriptionSet?>(null);

        var description = Describe(document.Text, found.Name, found.ActiveParameter);

        return Task.FromResult<SymbolDescriptionSet?>(
            description is null ? null : SymbolDescriptionSet.One(description));
    }

    /// <summary>One name, described from the intrinsics or from the program.</summary>
    private static SymbolDescription? Describe(string text, string name, int activeParameter)
    {
        if (Intrinsics.TryGetValue(name, out var intrinsic))
            return intrinsic.Describe(activeParameter);

        // The program's own procedures. A parse failure costs the description
        // and nothing else: the file is being written.
        try
        {
            var program = Parser.Parse(text);

            var procedure = program.Procedures.FirstOrDefault(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (procedure is null) return null;

            return FromProcedure(procedure, activeParameter);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>A SUB or FUNCTION the program declares.</summary>
    private static SymbolDescription FromProcedure(Procedure procedure, int activeParameter)
    {
        var parts = new List<SymbolPart>
        {
            SymbolPart.Keyword(procedure.IsFunction ? "FUNCTION" : "SUB"),
            SymbolPart.Plain(" "),
            SymbolPart.Name(procedure.Name),
            SymbolPart.Plain(" (")
        };

        for (var i = 0; i < procedure.Parameters.Count; i++)
        {
            if (i > 0) parts.Add(SymbolPart.Plain(", "));

            var parameter = procedure.Parameters[i];

            parts.Add(SymbolPart.Parameter(parameter.Name));
            parts.Add(SymbolPart.Plain(" AS "));
            parts.Add(SymbolPart.Type(TypeName(parameter.Type)));
        }

        parts.Add(SymbolPart.Plain(")"));

        if (procedure.IsFunction)
        {
            parts.Add(SymbolPart.Plain(" AS "));
            parts.Add(SymbolPart.Type(TypeName(procedure.ReturnType)));
        }

        return new SymbolDescription(parts)
        {
            Parameters = [.. procedure.Parameters.Select(
                p => new ParameterDescription(p.Name, TypeName(p.Type)))],
            ActiveParameter = activeParameter,
            Kind = procedure.IsFunction ? SymbolKind.Function : SymbolKind.Method
        };
    }

    private static string TypeName(BasicType type) => type switch
    {
        BasicType.Integer => "INTEGER",
        BasicType.Long => "LONG",
        BasicType.Single => "SINGLE",
        BasicType.Double => "DOUBLE",
        BasicType.String => "STRING",
        _ => "VOID"
    };

    /// <summary>The name at a position, if there is one.</summary>
    internal static string WordAt(string text, int position)
    {
        var at = Math.Clamp(position, 0, text.Length);

        var start = at;

        while (start > 0 && IsNamePart(text[start - 1])) start--;

        var end = at;

        // The trailing $ or % belongs to the name in BASIC.
        while (end < text.Length && IsNamePart(text[end])) end++;

        if (end < text.Length && text[end] is '$' or '%' or '&' or '!' or '#') end++;

        return end > start ? text[start..end] : "";
    }

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// The name that ends just before a position.
    ///
    /// What sits in front of an opening bracket. The trailing type character
    /// is part of the name in BASIC: LEFT$ and LEFT are not the same word.
    /// </summary>
    internal static string WordEndingAt(string text, int position)
    {
        var end = Math.Clamp(position, 0, text.Length);

        // Spaces are allowed between a name and its bracket.
        while (end > 0 && (text[end - 1] == ' ' || text[end - 1] == '\t')) end--;

        if (end > 0 && text[end - 1] is '$' or '%' or '&' or '!' or '#') end--;

        var start = end;

        while (start > 0 && IsNamePart(text[start - 1])) start--;

        if (start == end) return "";

        // The type character belongs to the name.
        var after = end;

        if (after < text.Length && text[after] is '$' or '%' or '&' or '!' or '#') after++;

        return text[start..after];
    }

    /// <summary>
    /// The call the position sits inside, and which argument is being written.
    /// </summary>
    internal static (string Name, int ActiveParameter)? CallAt(string text, int position)
    {
        var at = Math.Clamp(position, 0, text.Length);

        var depth = 0;
        var commas = 0;

        for (var i = at - 1; i >= 0; i--)
        {
            var c = text[i];

            if (c == ')') depth++;
            else if (c == ',' && depth == 0) commas++;
            else if (c == '(')
            {
                if (depth > 0) { depth--; continue; }

                // The name in front of the bracket is what is being called.
                // Ending at the bracket, not starting from it: WordAt looks
                // both ways, and from the bracket itself it finds nothing.
                var name = WordEndingAt(text, i);

                return name.Length == 0 ? null : (name, commas);
            }
            else if (c is '\n' or '\r')
            {
                // A call does not run past the end of its line in BASIC.
                return null;
            }
        }

        return null;
    }

    /// <summary>A built-in function, and what it takes.</summary>
    private sealed record Intrinsic(
        string Name,
        string ReturnType,
        IReadOnlyList<ParameterDescription> Parameters,
        string Documentation)
    {
        public SymbolDescription Describe(int activeParameter)
        {
            var parts = new List<SymbolPart> { SymbolPart.Name(Name) };

            if (Parameters.Count > 0)
            {
                parts.Add(SymbolPart.Plain(" ("));

                for (var i = 0; i < Parameters.Count; i++)
                {
                    if (i > 0) parts.Add(SymbolPart.Plain(", "));

                    parts.Add(SymbolPart.Parameter(Parameters[i].Name));
                    parts.Add(SymbolPart.Plain(" AS "));
                    parts.Add(SymbolPart.Type(Parameters[i].Type ?? "ANY"));
                }

                parts.Add(SymbolPart.Plain(")"));
            }

            parts.Add(SymbolPart.Plain(" AS "));
            parts.Add(SymbolPart.Type(ReturnType));

            return new SymbolDescription(parts)
            {
                Parameters = Parameters,
                ActiveParameter = activeParameter,
                Documentation = Documentation,
                Kind = SymbolKind.Function
            };
        }
    }
}
