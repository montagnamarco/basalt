using System.Globalization;
using System.Text;

namespace Basalt.QuickBasic.CodeGeneration;

/// <summary>
/// Turns a QuickBASIC program into Visual Basic .NET.
///
/// The two languages look alike, which is the trap: the differences are small
/// and each one silently changes what a program prints.
///
///   - PRINT puts a space before a positive number and one after it always;
///     VB.NET's Console.Write does neither, so the spacing is written out.
///   - A comparison gives -1 in QuickBASIC and True in VB.NET, and programs
///     do arithmetic on the result.
///   - DIM a(5) is six elements in both, but VB.NET needs Option Strict off
///     for the loose typing QuickBASIC programs rely on.
///   - A FUNCTION returns by assigning to its own name, which VB.NET still
///     allows, so that one carries across unchanged.
/// </summary>
public sealed class VisualBasicCodeGenerator : ICodeGenerator
{
    private readonly StringBuilder _output = new();

    private SymbolTable _symbols = null!;

    private int _indent;

    /// <summary>How many GOSUB call sites have been written.</summary>
    private int _gosubSites;

    /// <summary>Whether the body being written holds a GOSUB anywhere.</summary>
    private bool _mayGosub;

    private const string __gosubDepthName = "__gosubDepth";

    public string Id => "vbnet";

    public string DisplayName => "Visual Basic .NET";

    public string FileExtension => ".vb";

    public bool IsComplete => true;

    public string Generate(Program program, SymbolTable symbols)
    {
        _output.Clear();
        _symbols = symbols;
        _indent = 0;

        // Option Strict Off because QuickBASIC has no declarations to speak of
        // and mixes numeric types freely; Explicit Off because a variable is
        // whatever its first use makes it.
        Line("Option Strict Off");
        Line("Option Explicit Off");
        Line("");
        Line("Imports System");
        Line("");
        Line("Module QuickBasicProgram");
        _indent++;

        WriteHelpers();

        // The body is written aside first: how many GOSUB sites it holds is
        // only known once written, and the stack has to be declared above them.
        _mayGosub = program.Main.Any(HasGosub);

        var before = _output.Length;

        _indent++;

        foreach (var variable in symbols.Globals)
            WriteDeclaration(variable);

        foreach (var statement in program.Main)
            WriteStatement(statement);

        _indent--;

        var body = _output.ToString(before, _output.Length - before);

        _output.Length = before;

        Line("Sub Main()");
        _indent++;

        if (_gosubSites > 0)
        {
            Line($"Dim __gosub({Math.Max(_gosubSites, 8) * 4}) As Integer");
            Line($"Dim {__gosubDepthName} As Integer = 0");
        }

        _indent--;
        _output.Append(body);
        _indent++;

        if (_gosubSites > 0) WriteGosubDispatch();

        _indent--;
        Line("End Sub");

        foreach (var procedure in program.Procedures)
        {
            Line("");
            WriteProcedure(procedure);
        }

        _indent--;
        Line("End Module");

        return _output.ToString();
    }

    /// <summary>
    /// The few things QuickBASIC has that VB.NET does not.
    ///
    /// Printing especially: getting the spaces right is what makes the output
    /// match, and doing it at each PRINT would repeat this everywhere.
    /// </summary>
    private void WriteHelpers()
    {
        Line("' QuickBASIC prints a number with a space before it when positive");
        Line("' and one after it always. VB.NET does neither on its own.");
        Line("Private Sub QbPrint(ByVal value As Object)");
        _indent++;
        Line("If TypeOf value Is String Then");
        _indent++;
        Line("Console.Write(DirectCast(value, String))");
        _indent--;
        Line("Else");
        _indent++;
        Line("Dim number As Double = CDbl(value)");
        Line("Dim written As String");
        Line("If number = Math.Floor(number) AndAlso Math.Abs(number) < 1.0E+15 Then");
        _indent++;
        Line("written = CLng(number).ToString(Globalization.CultureInfo.InvariantCulture)");
        _indent--;
        Line("Else");
        _indent++;
        Line("written = number.ToString(\"G6\", Globalization.CultureInfo.InvariantCulture).Replace(\"E\", \"e\")");
        _indent--;
        Line("End If");
        Line("If number >= 0 Then");
        _indent++;
        Line("Console.Write(\" \" & written & \" \")");
        _indent--;
        Line("Else");
        _indent++;
        Line("Console.Write(written & \" \")");
        _indent--;
        Line("End If");
        _indent--;
        Line("End If");
        _indent--;
        Line("End Sub");
        Line("");

        Line("' A comparison is -1 in QuickBASIC, and programs count on it.");
        Line("Private Function QbBool(ByVal value As Boolean) As Integer");
        _indent++;
        Line("If value Then Return -1");
        Line("Return 0");
        _indent--;
        Line("End Function");
        Line("");

        Line("Private Function QbInput() As String");
        _indent++;
        Line("Dim typed As String = Console.ReadLine()");
        Line("If typed Is Nothing Then Return \"\"");
        Line("Return typed");
        _indent--;
        Line("End Function");
        Line("");
    }

    /// <summary>Whether a statement, or anything inside it, is a GOSUB.</summary>
    private static bool HasGosub(Statement statement) => statement switch
    {
        GosubStatement => true,
        IfStatement conditional =>
            conditional.Then.Any(HasGosub)
            || conditional.ElseIfs.Any(b => b.Body.Any(HasGosub))
            || (conditional.Else?.Any(HasGosub) ?? false),
        ForStatement loop => loop.Body.Any(HasGosub),
        WhileStatement loop => loop.Body.Any(HasGosub),
        DoStatement loop => loop.Body.Any(HasGosub),
        SelectStatement select =>
            select.Cases.Any(c => c.Body.Any(HasGosub))
            || (select.Otherwise?.Any(HasGosub) ?? false),
        _ => false
    };

    /// <summary>
    /// Where a Return inside a GOSUB comes back through.
    ///
    /// VB.NET has no GoSub, so the way back is a Select Case on the number
    /// the call site pushed, guarded so it is only reached by jumping to it.
    /// </summary>
    private void WriteGosubDispatch()
    {
        Line("");
        Line("If False Then");
        _indent++;
        Line("__gosub_dispatch:");
        Line($"{__gosubDepthName} -= 1");
        Line($"Select Case __gosub({__gosubDepthName})");
        _indent++;

        for (var site = 1; site <= _gosubSites; site++)
        {
            Line($"Case {site}");
            _indent++;
            Line($"GoTo __gosub_return_{site}");
            _indent--;
        }

        _indent--;
        Line("End Select");
        _indent--;
        Line("End If");
    }

    private void WriteDeclaration(VariableInfo variable)
    {
        // DIM a(5) is six elements in both languages, so the bound carries
        // across as written.
        Line(variable.IsArray
            ? $"Dim {Safe(variable.Name)}({variable.Length - 1}) As {VbType(variable.Type)}"
            : $"Dim {Safe(variable.Name)} As {VbType(variable.Type)} = {Zero(variable.Type)}");
    }

    private void WriteProcedure(Procedure procedure)
    {
        var parameters = string.Join(", ", procedure.Parameters.Select(p =>
            // ByRef is the QuickBASIC default, and programs use it to hand
            // back more than one value.
            $"ByRef {Safe(p.Name)} As {VbType(p.Type)}"));

        Line(procedure.IsFunction
            ? $"Private Function {Safe(procedure.Name)}({parameters}) As {VbType(procedure.ReturnType)}"
            : $"Private Sub {Safe(procedure.Name)}({parameters})");

        _indent++;

        foreach (var local in _symbols.LocalsOf(procedure.Name))
        {
            if (procedure.Parameters.Any(p =>
                p.Name.Equals(local.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // A function's own name is its result, and VB.NET declares it for
            // us; declaring it again would be an error.
            if (local.Name.Equals(procedure.Name, StringComparison.OrdinalIgnoreCase)) continue;

            WriteDeclaration(local);
        }

        foreach (var statement in procedure.Body)
            WriteStatement(statement);

        _indent--;

        Line(procedure.IsFunction ? "End Function" : "End Sub");
    }

    private void WriteStatement(Statement statement)
    {
        switch (statement)
        {
            case Assignment assignment:
                Line($"{Target(assignment.Target)} = {Value(assignment.Value)}");
                break;

            case Declaration declaration when declaration.Bounds is { Count: > 0 } bounds:
                Line($"ReDim {Safe(declaration.Name)}({Value(bounds[0])})");
                break;

            case Declaration:
                // Already declared at the top of the procedure.
                break;

            case PrintStatement print:
                foreach (var value in print.Values)
                    Line($"QbPrint({Value(value)})");

                if (!print.TrailingSemicolon) Line("Console.WriteLine()");
                break;

            case InputStatement input:
                WriteInput(input);
                break;

            case IfStatement conditional:
                WriteIf(conditional);
                break;

            case ForStatement loop:
                Line($"For {Safe(loop.Variable)} = {Value(loop.From)} To {Value(loop.To)}"
                   + (loop.Step is null ? "" : $" Step {Value(loop.Step)}"));
                _indent++;
                foreach (var inner in loop.Body) WriteStatement(inner);
                _indent--;
                Line($"Next {Safe(loop.Variable)}");
                break;

            case WhileStatement loop:
                Line($"Do While {Truth(loop.Condition)}");
                _indent++;
                foreach (var inner in loop.Body) WriteStatement(inner);
                _indent--;
                Line("Loop");
                break;

            case DoStatement loop:
                WriteDo(loop);
                break;

            case SelectStatement select:
                WriteSelect(select);
                break;

            case GotoStatement jump:
                Line($"GoTo {Safe(jump.Label)}");
                break;

            case GosubStatement call:
                // VB.NET removed GoSub, so each call site is numbered and
                // Return dispatches back on that number, as the C target
                // does. Written out rather than refused: a program using
                // GOSUB is exactly the kind of old program this target is for.
                var site = ++_gosubSites;

                Line($"__gosub({__gosubDepthName}) = {site}");
                Line($"{__gosubDepthName} += 1");
                Line($"GoTo {Safe(call.Label)}");
                Line($"__gosub_return_{site}:");
                break;

            case LabelStatement label:
                // A label sits at the left margin in VB.NET.
                var saved = _indent;
                _indent = 0;
                Line($"{Safe(label.Name)}:");
                _indent = saved;
                break;

            case CallStatement call:
                Line($"{Safe(call.Name)}({string.Join(", ", call.Arguments.Select(Value))})");
                break;

            case ReturnStatement { Value: { } value }:
                Line($"Return {Value(value)}");
                break;

            case ReturnStatement:
                if (_gosubSites > 0 || _mayGosub)
                {
                    Line($"If {__gosubDepthName} > 0 Then GoTo __gosub_dispatch");
                }

                Line("Return");
                break;

            case ExitStatement exit:
                Line(exit.What.ToUpperInvariant() switch
                {
                    "FOR" => "Exit For",
                    "DO" => "Exit Do",
                    "WHILE" => "Exit Do",
                    "SUB" => "Exit Sub",
                    "FUNCTION" => "Exit Function",
                    _ => "Exit Do"
                });
                break;

            case EndStatement:
                Line("End");
                break;
        }
    }

    private void WriteInput(InputStatement input)
    {
        if (input.Prompt is { Length: > 0 } prompt)
            Line($"Console.Write({Quote(prompt)})");

        // INPUT a, b reads one line and splits it on commas.
        Line("Dim __typed As String = QbInput()");
        Line("Dim __parts As String() = __typed.Split(\",\"c)");

        for (var i = 0; i < input.Targets.Count; i++)
        {
            var name = input.Targets[i];
            var part = $"If(__parts.Length > {i}, __parts({i}).Trim(), \"\")";

            Line(TypeSuffix.Of(name) == BasicType.String
                ? $"{Safe(name)} = {part}"
                : $"{Safe(name)} = Val({part})");
        }
    }

    private void WriteIf(IfStatement statement)
    {
        Line($"If {Truth(statement.Condition)} Then");
        _indent++;
        foreach (var inner in statement.Then) WriteStatement(inner);
        _indent--;

        foreach (var (condition, body) in statement.ElseIfs)
        {
            Line($"ElseIf {Truth(condition)} Then");
            _indent++;
            foreach (var inner in body) WriteStatement(inner);
            _indent--;
        }

        if (statement.Else is { } otherwise)
        {
            Line("Else");
            _indent++;
            foreach (var inner in otherwise) WriteStatement(inner);
            _indent--;
        }

        Line("End If");
    }

    private void WriteDo(DoStatement loop)
    {
        if (loop.Condition is null)
        {
            Line("Do");
            _indent++;
            foreach (var inner in loop.Body) WriteStatement(inner);
            _indent--;
            Line("Loop");
            return;
        }

        var keyword = loop.Until ? "Until" : "While";

        Line(loop.TestAtEnd ? "Do" : $"Do {keyword} {Truth(loop.Condition)}");
        _indent++;
        foreach (var inner in loop.Body) WriteStatement(inner);
        _indent--;
        Line(loop.TestAtEnd ? $"Loop {keyword} {Truth(loop.Condition)}" : "Loop");
    }

    private void WriteSelect(SelectStatement select)
    {
        Line($"Select Case {Value(select.Value)}");
        _indent++;

        foreach (var (values, body) in select.Cases)
        {
            Line($"Case {string.Join(", ", values.Select(Value))}");
            _indent++;
            foreach (var inner in body) WriteStatement(inner);
            _indent--;
        }

        if (select.Otherwise is { } otherwise)
        {
            Line("Case Else");
            _indent++;
            foreach (var inner in otherwise) WriteStatement(inner);
            _indent--;
        }

        _indent--;
        Line("End Select");
    }

    // Expressions

    private string Target(Expression target) => target switch
    {
        VariableReference variable => Safe(variable.Name),
        IndexOrCall indexed => $"{Safe(indexed.Name)}(CInt({Value(indexed.Arguments[0])}))",
        _ => "__unsupported"
    };

    private string Value(Expression expression) => expression switch
    {
        NumberLiteral number => Number(number),

        StringLiteral text => Quote(text.Value),

        VariableReference variable => Safe(variable.Name),

        IndexOrCall call => _symbols.IsArray(call.Name)
            ? $"{Safe(call.Name)}(CInt({Value(call.Arguments[0])}))"
            : LibraryCall(call) ?? $"{Safe(call.Name)}({string.Join(", ", call.Arguments.Select(Value))})",

        Unary { Operator: "NOT" } unary => $"(Not CLng({Value(unary.Operand)}))",
        Unary unary => $"({unary.Operator}{Value(unary.Operand)})",

        Binary binary => BinaryValue(binary),

        _ => "0"
    };

    private string BinaryValue(Binary binary)
    {
        var left = Value(binary.Left);
        var right = Value(binary.Right);

        // A comparison must come out as -1, not as True, since QuickBASIC
        // programs go on to do arithmetic with it.
        if (binary.Operator is "=" or "<>" or "<" or ">" or "<=" or ">=")
            return $"QbBool({left} {binary.Operator} {right})";

        return binary.Operator switch
        {
            // Bitwise in QuickBASIC, which is why true is all ones.
            "AND" => $"(CLng({left}) And CLng({right}))",
            "OR" => $"(CLng({left}) Or CLng({right}))",
            "XOR" => $"(CLng({left}) Xor CLng({right}))",

            "MOD" => $"(CLng({left}) Mod CLng({right}))",

            // Integer division in QuickBASIC is VB.NET's backslash too, but
            // the operands must be whole first.
            "\\" => $"(CLng({left}) \\ CLng({right}))",

            // Always a real division, as QuickBASIC has it.
            "/" => $"(CDbl({left}) / CDbl({right}))",

            "^" => $"({left} ^ {right})",

            _ => $"({left} {binary.Operator} {right})"
        };
    }

    /// <summary>
    /// A condition, as VB.NET wants it.
    ///
    /// QuickBASIC tests a number against zero; VB.NET wants a Boolean, and
    /// Option Strict Off would convert silently but not always as intended.
    /// </summary>
    private string Truth(Expression condition) => $"(({Value(condition)}) <> 0)";

    /// <summary>
    /// A built-in function, where VB.NET spells it differently.
    ///
    /// Most carry across because VB.NET kept them; the ones that did not are
    /// written out.
    /// </summary>
    private string? LibraryCall(IndexOrCall call)
    {
        string Argument(int index) =>
            index < call.Arguments.Count ? Value(call.Arguments[index]) : "0";

        return call.Name.ToUpperInvariant() switch
        {
            "LEN" => $"Len({Argument(0)})",
            "MID$" => call.Arguments.Count > 2
                ? $"Mid({Argument(0)}, CInt({Argument(1)}), CInt({Argument(2)}))"
                : $"Mid({Argument(0)}, CInt({Argument(1)}))",
            "LEFT$" => $"Microsoft.VisualBasic.Left({Argument(0)}, CInt({Argument(1)}))",
            "RIGHT$" => $"Microsoft.VisualBasic.Right({Argument(0)}, CInt({Argument(1)}))",
            "CHR$" => $"Chr(CInt({Argument(0)}))",
            "ASC" => $"Asc({Argument(0)})",
            "VAL" => $"Val({Argument(0)})",

            // STR$ writes the leading space QuickBASIC writes; VB.NET's Str
            // does the same, so it carries across.
            "STR$" => $"Str({Argument(0)})",

            "ABS" => $"Math.Abs({Argument(0)})",
            "INT" => $"Math.Floor({Argument(0)})",
            "FIX" => $"Math.Truncate({Argument(0)})",
            "SQR" => $"Math.Sqrt({Argument(0)})",
            "SGN" => $"Math.Sign({Argument(0)})",
            "CINT" => $"CInt({Argument(0)})",
            "UCASE$" => $"UCase({Argument(0)})",
            "LCASE$" => $"LCase({Argument(0)})",
            "LTRIM$" => $"LTrim({Argument(0)})",
            "RTRIM$" => $"RTrim({Argument(0)})",
            "SPACE$" => $"Space(CInt({Argument(0)}))",
            "INSTR" => $"InStr({Argument(0)}, {Argument(1)})",
            "RND" => "Rnd()",
            "TIMER" => "Timer",

            _ => null
        };
    }

    private static string Number(NumberLiteral number) =>
        number.Value == Math.Floor(number.Value) && Math.Abs(number.Value) < 1e15
            ? ((long)number.Value).ToString(CultureInfo.InvariantCulture)
            : number.Value.ToString("R", CultureInfo.InvariantCulture);

    private static string Quote(string text) =>
        "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Zero(BasicType type) =>
        type == BasicType.String ? "\"\"" : "0";

    private static string VbType(BasicType type) => type switch
    {
        BasicType.Integer => "Short",
        BasicType.Long => "Integer",
        BasicType.Single => "Single",
        BasicType.Double => "Double",
        BasicType.String => "String",
        _ => "Object"
    };

    /// <summary>
    /// A QuickBASIC name as VB.NET can take it.
    ///
    /// The type suffixes mean the same thing in VB.NET, but a name can also
    /// collide with a keyword, so it is bracketed.
    /// </summary>
    private static string Safe(string name)
    {
        var bare = name.TrimEnd('$', '%', '&', '!', '#');

        return $"[{bare}]";
    }

    private void Line(string text)
    {
        if (text.Length > 0) _output.Append(new string(' ', _indent * 4));

        _output.Append(text).Append('\n');
    }
}
