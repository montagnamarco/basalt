using System.Text;

namespace Basalt.Workspace.Debugging;

/// <summary>
/// What netcoredbg reports, written as Visual Basic: the debugger speaks C#, so a VB program's Variables, Watch
/// and Call Stack read "int", "List&lt;int&gt;", "this", "null" and "VB$StateMachine_1_ComputeAsync.MoveNext()".
/// </summary>
/// <remarks>
/// Only the spelling changes, never a value: namespaces stay, so "System.Collections.Generic.List(Of Integer)"
/// is what Visual Studio's VB debugger shows too. Text this does not recognise is returned as it came.
/// </remarks>
public static class VbDisplay
{
    private static readonly Dictionary<string, string> Keywords = new(StringComparer.Ordinal)
    {
        ["int"] = "Integer", ["long"] = "Long", ["short"] = "Short", ["byte"] = "Byte", ["sbyte"] = "SByte",
        ["uint"] = "UInteger", ["ulong"] = "ULong", ["ushort"] = "UShort", ["double"] = "Double",
        ["float"] = "Single", ["decimal"] = "Decimal", ["bool"] = "Boolean", ["char"] = "Char",
        ["string"] = "String", ["object"] = "Object", ["System.DateTime"] = "Date",
    };

    /// <summary>The CLR names netcoredbg uses for parameters in a frame name.</summary>
    private static readonly Dictionary<string, string> ClrNames = new(StringComparer.Ordinal)
    {
        ["Int32"] = "Integer", ["Int64"] = "Long", ["Int16"] = "Short", ["Byte"] = "Byte", ["SByte"] = "SByte",
        ["UInt32"] = "UInteger", ["UInt64"] = "ULong", ["UInt16"] = "UShort", ["Double"] = "Double",
        ["Single"] = "Single", ["Decimal"] = "Decimal", ["Boolean"] = "Boolean", ["Char"] = "Char",
        ["String"] = "String", ["Object"] = "Object", ["DateTime"] = "Date",
    };

    /// <summary>A variable's name: C#'s "this" is VB's "Me".</summary>
    public static string Name(string name) => name == "this" ? "Me" : name;

    /// <summary>A type as VB writes it: "Integer()", "List(Of Integer)", "Dictionary(Of String, Integer)", "Integer(,)".</summary>
    public static string Type(string type)
    {
        if (type.Length == 0) return type;

        var builder = new StringBuilder(type.Length + 8);
        var word = new StringBuilder();
        var inArrayRank = false;

        void FlushWord()
        {
            if (word.Length == 0) return;

            var text = word.ToString();
            builder.Append(Keywords.TryGetValue(text, out var vb) ? vb : ClrNames.TryGetValue(text, out var clr) ? clr : text);
            word.Clear();
        }

        foreach (var c in type)
        {
            switch (c)
            {
                case '<':
                    FlushWord();
                    builder.Append("(Of ");
                    break;
                case '>':
                    FlushWord();
                    builder.Append(')');
                    break;
                case '[':
                    FlushWord();
                    inArrayRank = true;
                    builder.Append('(');
                    break;
                case ']':
                    FlushWord();
                    inArrayRank = false;
                    builder.Append(')');
                    break;
                case ',':
                    FlushWord();

                    // A generic's arguments read "String, Integer"; an array's rank "(,)".
                    builder.Append(inArrayRank ? "," : ", ");
                    break;
                case ' ':
                case '?':
                    FlushWord();
                    if (c == '?') builder.Append('?');
                    break;
                default:
                    word.Append(c);
                    break;
            }
        }

        FlushWord();

        return builder.ToString();
    }

    /// <summary>A value as VB shows it: Nothing, True and False, and the type inside "{…}" in VB spelling.</summary>
    public static string Value(string value)
    {
        switch (value)
        {
            case "null":
                return "Nothing";
            case "true":
                return "True";
            case "false":
                return "False";
        }

        // "{int[3]}" is an array of three: VB declares that by upper bound, so the length is said in words,
        // as Visual Studio does.
        if (value.Length > 2 && value[0] == '{' && value[^1] == '}')
        {
            var inner = value[1..^1];
            var open = inner.LastIndexOf('[');

            if (open > 0 && inner[^1] == ']' && int.TryParse(inner[(open + 1)..^1], out var length))
                return $"{{Length={length}}}";

            if (!inner.Contains(' ') && !inner.Contains('"')) return "{" + Type(inner) + "}";
        }

        return value;
    }

    /// <summary>
    /// A call stack entry as the source names it: an Async or Iterator method by its own name rather than its
    /// state machine's MoveNext, a lambda as one, and parameters as "n As Integer".
    /// </summary>
    public static string FrameName(string name)
    {
        var open = name.IndexOf('(');
        var close = name.LastIndexOf(')');

        // Something not shaped as "Type.Method(parameters)" is shown as it came.
        if (open >= 0 && close < open) return name;

        var path = open < 0 ? name : name[..open];
        var parameters = open < 0 ? null : name[(open + 1)..close];

        // "Program.VB$StateMachine_1_ComputeAsync.MoveNext" → "Program.ComputeAsync"
        const string machine = "VB$StateMachine_";
        var at = path.IndexOf(machine, StringComparison.Ordinal);

        if (at >= 0)
        {
            var rest = path[(at + machine.Length)..];
            var underscore = rest.IndexOf('_');
            var method = underscore >= 0 ? rest[(underscore + 1)..] : rest;
            var dot = method.IndexOf('.');
            if (dot >= 0) method = method[..dot];

            return path[..at] + method + "()";
        }

        // "Program._Closure$__._Lambda$__0-0" → "Program.Lambda"
        if (path.Contains("_Lambda$__", StringComparison.Ordinal))
        {
            var closure = path.IndexOf("._Closure$__", StringComparison.Ordinal);
            var owner = closure >= 0 ? path[..closure] : path[..path.IndexOf("._Lambda$__", StringComparison.Ordinal)];

            return owner + ".Lambda()";
        }

        if (parameters is null) return name;

        var written = SplitParameters(parameters)
            .Select(parameter =>
            {
                var space = parameter.LastIndexOf(' ');
                if (space < 0) return parameter;

                var type = parameter[..space];
                var parameterName = parameter[(space + 1)..];

                // "ref Int32 a" is VB's "ByRef a As Integer"; "out" and "in" are by reference to VB too.
                var byRef = "";

                foreach (var modifier in (string[])["ref ", "out ", "in "])
                {
                    if (!type.StartsWith(modifier, StringComparison.Ordinal)) continue;

                    byRef = "ByRef ";
                    type = type[modifier.Length..];
                }

                return $"{byRef}{parameterName} As {(ClrNames.TryGetValue(type, out var vb) ? vb : Type(type))}";
            });

        return $"{path}({string.Join(", ", written)})";
    }

    /// <summary>The parameters of a frame name, split on the commas between them, not on those inside a generic type.</summary>
    private static IEnumerable<string> SplitParameters(string parameters)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            switch (parameters[i])
            {
                case '<' or '[' or '(':
                    depth++;
                    break;
                case '>' or ']' or ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    if (parameters[start..i].Trim() is { Length: > 0 } part) yield return part;
                    start = i + 1;
                    break;
            }
        }

        if (parameters[start..].Trim() is { Length: > 0 } last) yield return last;
    }
}
