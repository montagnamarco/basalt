using System.Globalization;
using System.Text;

namespace Basalt.QuickBasic;

/// <summary>
/// Turns a QuickBASIC program into C.
///
/// C rather than machine code because it buys a working optimiser and every
/// target clang supports for the cost of writing text; the alternative is an
/// instruction selector and a register allocator per architecture, which is
/// the whole of a compiler back end.
///
/// The generated code is meant to be read: a user who wants to know what their
/// program does can look, and a bug here is easier to find in C than in
/// assembly.
/// </summary>
public sealed class CodeWriter
{
    private readonly StringBuilder _output = new();
    private readonly Program _program;
    private readonly SymbolTable _symbols;
    private int _indent;

    /// <summary>
    /// How many GOSUB call sites have been written.
    ///
    /// Each gets a number, so RETURN can switch on it to find its way back;
    /// C has no GOSUB of its own.
    /// </summary>
    private int _gosubSites;

    /// <summary>
    /// Whether what is being written is the top level.
    ///
    /// A bare RETURN means different things there: main must give a value
    /// back, and QuickBASIC ends the program rather than returning from
    /// something.
    /// </summary>
    private bool _inMain;
    private int _labelCounter;

    private CodeWriter(Program program, SymbolTable symbols)
    {
        _program = program;
        _symbols = symbols;
    }

    public static string Generate(Program program, SymbolTable symbols)
    {
        var writer = new CodeWriter(program, symbols);
        writer.WriteProgram();

        return writer._output.ToString();
    }

    private void WriteProgram()
    {
        WritePrelude();

        // Procedures are declared before use so that order in the source does
        // not matter, as it does not in QuickBASIC.
        foreach (var procedure in _program.Procedures)
        {
            Line($"{Signature(procedure)};");
        }

        if (_program.Procedures.Count > 0) Line("");

        foreach (var variable in _symbols.Globals)
        {
            Line($"static {CType(variable.Type)} {Mangle(variable.Name)}{ArraySuffix(variable)};");
        }

        if (_symbols.Globals.Count > 0) Line("");

        foreach (var procedure in _program.Procedures) WriteProcedure(procedure);

        // Main's body is written to one side first, because how many GOSUB
        // sites it holds is only known once it has been written, and the
        // stack that serves them has to be declared before them.
        var before = _output.Length;

        _inMain = true;
        _indent++;
        foreach (var statement in _program.Main) WriteStatement(statement);
        _indent--;
        _inMain = false;

        var body = _output.ToString(before, _output.Length - before);

        _output.Length = before;

        Line("int main(void)");
        Line("{");
        _indent++;

        if (_gosubSites > 0)
        {
            Line($"int qb_gosub_stack[{Math.Max(_gosubSites, 8) * 4}];");
            Line("int qb_gosub_depth = 0;");
            Line("");
        }

        _output.Append(body);

        if (_gosubSites > 0) WriteGosubDispatch();

        Line("return 0;");
        _indent--;
        Line("}");
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
    /// Where a RETURN inside a GOSUB comes back through.
    ///
    /// A switch on the number the call site pushed. Written at the end of
    /// main, after the labels it jumps to exist.
    /// </summary>
    private void WriteGosubDispatch()
    {
        Line("");
        Line("if (0) {");
        _indent++;
        Line("qb_gosub_dispatch:;");
        Line("switch (qb_gosub_stack[--qb_gosub_depth]) {");
        _indent++;

        for (var site = 1; site <= _gosubSites; site++)
            Line($"case {site}: goto qb_gosub_return_{site};");

        _indent--;
        Line("}");
        _indent--;
        Line("}");
    }

    /// <summary>
    /// The runtime the generated code leans on.
    ///
    /// Written out rather than shipped as a library so that the C file is
    /// self-contained: it compiles with nothing but a C compiler, which is
    /// what makes cross-compiling a matter of one clang flag.
    /// </summary>
    private void WritePrelude()
    {
        Line("/* Generated from QuickBASIC by Basalt. */");
        Line("#include <stdio.h>");
        Line("#include <stdlib.h>");
        Line("#include <string.h>");
        Line("#include <ctype.h>");
        Line("#include <math.h>");
        Line("");
        Line("/* Strings are counted rather than null-terminated so that a");
        Line("   QuickBASIC string can hold anything, as it can in the original. */");
        Line("typedef struct { char *data; int length; } qb_string;");
        Line("");
        Line("static qb_string qb_str_new(const char *text, int length)");
        Line("{");
        Line("    qb_string s;");
        Line("    s.data = (char *)malloc((size_t)length + 1);");
        Line("    if (s.data == NULL) { fputs(\"out of memory\\n\", stderr); exit(1); }");
        Line("    memcpy(s.data, text, (size_t)length);");
        Line("    s.data[length] = '\\0';");
        Line("    s.length = length;");
        Line("    return s;");
        Line("}");
        Line("");
        Line("static qb_string qb_str_lit(const char *text) { return qb_str_new(text, (int)strlen(text)); }");
        Line("");
        Line("static qb_string qb_str_concat(qb_string a, qb_string b)");
        Line("{");
        Line("    qb_string s;");
        Line("    s.data = (char *)malloc((size_t)(a.length + b.length) + 1);");
        Line("    if (s.data == NULL) { fputs(\"out of memory\\n\", stderr); exit(1); }");
        Line("    memcpy(s.data, a.data, (size_t)a.length);");
        Line("    memcpy(s.data + a.length, b.data, (size_t)b.length);");
        Line("    s.data[a.length + b.length] = '\\0';");
        Line("    s.length = a.length + b.length;");
        Line("    return s;");
        Line("}");
        Line("");
        Line("static int qb_str_compare(qb_string a, qb_string b)");
        Line("{");
        Line("    int shortest = a.length < b.length ? a.length : b.length;");
        Line("    int order = memcmp(a.data, b.data, (size_t)shortest);");
        Line("    if (order != 0) return order;");
        Line("    return a.length - b.length;");
        Line("}");
        Line("");
        Line("/* QuickBASIC prints a number with a leading space when positive and");
        Line("   a trailing one always, which is what makes its output look right. */");
        Line("static void qb_print_number(double value)");
        Line("{");
        Line("    if (value == (double)(long long)value)");
        Line("        printf(value >= 0 ? \" %lld \" : \"%lld \", (long long)value);");
        Line("    else");
        Line("        printf(value >= 0 ? \" %g \" : \"%g \", value);");
        Line("}");
        Line("");
        // The functions a program can call without defining them. Written
        // here rather than left undefined: the interpreter has them, and a
        // program has to behave the same way built as it does debugged.
        Line("static int qb_len(qb_string s) { return s.length; }");
        Line("");
        Line("static qb_string qb_mid(qb_string s, int start, int count)");
        Line("{");
        Line("    /* QuickBASIC counts from one, and asks for what is left when");
        Line("       no count is given, which the caller passes as -1. */");
        Line("    if (start < 1 || start > s.length) return qb_str_lit(\"\");");
        Line("    int from = start - 1;");
        Line("    int take = count < 0 ? s.length - from : count;");
        Line("    if (take > s.length - from) take = s.length - from;");
        Line("    if (take < 0) take = 0;");
        Line("    return qb_str_new(s.data + from, take);");
        Line("}");
        Line("");
        Line("static qb_string qb_left(qb_string s, int count)");
        Line("{");
        Line("    if (count < 0) count = 0;");
        Line("    if (count > s.length) count = s.length;");
        Line("    return qb_str_new(s.data, count);");
        Line("}");
        Line("");
        Line("static qb_string qb_right(qb_string s, int count)");
        Line("{");
        Line("    if (count < 0) count = 0;");
        Line("    if (count > s.length) count = s.length;");
        Line("    return qb_str_new(s.data + (s.length - count), count);");
        Line("}");
        Line("");
        Line("static qb_string qb_chr(int code)");
        Line("{");
        Line("    char c = (char)code;");
        Line("    return qb_str_new(&c, 1);");
        Line("}");
        Line("");
        Line("static int qb_asc(qb_string s) { return s.length == 0 ? 0 : (unsigned char)s.data[0]; }");
        Line("");
        Line("static double qb_val(qb_string s)");
        Line("{");
        Line("    char buffer[64];");
        Line("    int n = s.length < 63 ? s.length : 63;");
        Line("    memcpy(buffer, s.data, (size_t)n);");
        Line("    buffer[n] = '\\0';");
        Line("    return atof(buffer);");
        Line("}");
        Line("");
        Line("static qb_string qb_str(double value)");
        Line("{");
        Line("    char buffer[64];");
        Line("    if (value == (double)(long long)value)");
        Line("        snprintf(buffer, sizeof buffer, value >= 0 ? \" %lld\" : \"%lld\", (long long)value);");
        Line("    else");
        Line("        snprintf(buffer, sizeof buffer, value >= 0 ? \" %g\" : \"%g\", value);");
        Line("    return qb_str_lit(buffer);");
        Line("}");
        Line("");
        Line("static qb_string qb_case(qb_string s, int upper)");
        Line("{");
        Line("    qb_string r = qb_str_new(s.data, s.length);");
        Line("    for (int i = 0; i < r.length; i++)");
        Line("        r.data[i] = (char)(upper ? toupper((unsigned char)r.data[i])");
        Line("                                 : tolower((unsigned char)r.data[i]));");
        Line("    return r;");
        Line("}");
        Line("");
        Line("static int qb_instr(qb_string haystack, qb_string needle)");
        Line("{");
        Line("    /* Counted from one, and zero when there is nothing to find. */");
        Line("    if (needle.length == 0) return 1;");
        Line("    for (int i = 0; i + needle.length <= haystack.length; i++)");
        Line("        if (memcmp(haystack.data + i, needle.data, (size_t)needle.length) == 0)");
        Line("            return i + 1;");
        Line("    return 0;");
        Line("}");
        Line("");
        Line("static qb_string qb_space(int count)");
        Line("{");
        Line("    if (count < 0) count = 0;");
        Line("    qb_string s;");
        Line("    s.data = (char *)malloc((size_t)count + 1);");
        Line("    if (s.data == NULL) { fputs(\"out of memory\\n\", stderr); exit(1); }");
        Line("    memset(s.data, ' ', (size_t)count);");
        Line("    s.data[count] = '\\0';");
        Line("    s.length = count;");
        Line("    return s;");
        Line("}");
        Line("");
        Line("static qb_string qb_trim(qb_string s, int left)");
        Line("{");
        Line("    int from = 0, to = s.length;");
        Line("    if (left) { while (from < to && s.data[from] == ' ') from++; }");
        Line("    else { while (to > from && s.data[to - 1] == ' ') to--; }");
        Line("    return qb_str_new(s.data + from, to - from);");
        Line("}");
        Line("");
        Line("static void qb_print_string(qb_string s) { fwrite(s.data, 1, (size_t)s.length, stdout); }");
        Line("");
        Line("static qb_string qb_input_string(void)");
        Line("{");
        Line("    char buffer[4096];");
        Line("    if (fgets(buffer, sizeof buffer, stdin) == NULL) return qb_str_lit(\"\");");
        Line("    size_t length = strlen(buffer);");
        Line("    while (length > 0 && (buffer[length - 1] == '\\n' || buffer[length - 1] == '\\r')) length--;");
        Line("    return qb_str_new(buffer, (int)length);");
        Line("}");
        Line("");
        Line("static double qb_input_number(void)");
        Line("{");
        Line("    qb_string s = qb_input_string();");
        Line("    double value = atof(s.data);");
        Line("    free(s.data);");
        Line("    return value;");
        Line("}");
        Line("");
    }

    private void WriteProcedure(Procedure procedure)
    {
        Line(Signature(procedure));
        Line("{");
        _indent++;

        var locals = _symbols.LocalsOf(procedure.Name);

        foreach (var local in locals.Where(l =>
                     // Parameters are already declared by the signature.
                     !procedure.Parameters.Any(
                         p => p.Name.Equals(l.Name, StringComparison.OrdinalIgnoreCase))
                     // A function's own name is its result: assigning to it
                     // inside the body records it as a local, but it is
                     // declared below as the variable that gets returned.
                     && !(procedure.IsFunction
                          && l.Name.Equals(procedure.Name, StringComparison.OrdinalIgnoreCase))))
        {
            Line($"{CType(local.Type)} {Mangle(local.Name)}{ArraySuffix(local)} = {Zero(local)};");
        }

        // A function returns through a variable named after itself, as in
        // QuickBASIC, so that "Add = 1" inside FUNCTION Add sets the result.
        if (procedure.IsFunction)
        {
            Line($"{CType(procedure.ReturnType)} {Mangle(procedure.Name)} = "
               + $"{ZeroOf(procedure.ReturnType)};");
        }

        foreach (var statement in procedure.Body) WriteStatement(statement);

        if (procedure.IsFunction) Line($"return {Mangle(procedure.Name)};");

        _indent--;
        Line("}");
        Line("");
    }

    private string Signature(Procedure procedure)
    {
        var parameters = procedure.Parameters.Count == 0
            ? "void"
            : string.Join(", ", procedure.Parameters.Select(p =>
                $"{CType(p.Type)} {(p.IsArray ? "*" : "")}{Mangle(p.Name)}"));

        return $"static {CType(procedure.ReturnType)} {Mangle(procedure.Name)}({parameters})";
    }

    private void WriteStatement(Statement statement)
    {
        switch (statement)
        {
            case Declaration declaration:
                // Locals are declared at the top of the procedure; a DIM in
                // the body has already been counted there.
                if (_symbols.IsGlobal(declaration.Name)) break;
                break;

            case Assignment assignment:
                WriteAssignment(assignment);
                break;

            case PrintStatement print:
                WritePrint(print);
                break;

            case InputStatement input:
                WriteInput(input);
                break;

            case IfStatement ifStatement:
                WriteIf(ifStatement);
                break;

            case ForStatement forStatement:
                WriteFor(forStatement);
                break;

            case WhileStatement whileStatement:
                Line($"while ({Condition(whileStatement.Condition)})");
                WriteBlock(whileStatement.Body);
                break;

            case DoStatement doStatement:
                WriteDo(doStatement);
                break;

            case SelectStatement select:
                WriteSelect(select);
                break;

            case CallStatement call:
                Line($"{Mangle(call.Name)}({string.Join(", ", call.Arguments.Select(Value))});");
                break;

            case LabelStatement label:
                // Labels sit at the margin, as C conventionally writes them.
                _output.AppendLine($"{Mangle(label.Name)}:;");
                break;

            case GotoStatement jump:
                Line($"goto {Mangle(jump.Label)};");
                break;

            case GosubStatement call:
                // C has no GOSUB, so each site is numbered: the number is
                // pushed, the label jumped to, and RETURN switches on it to
                // get back here. Portable, unlike a computed goto.
                var site = ++_gosubSites;

                Line($"qb_gosub_stack[qb_gosub_depth++] = {site};");
                Line($"goto {Mangle(call.Label)};");
                _output.AppendLine($"qb_gosub_return_{site}:;");
                break;

            case ReturnStatement { Value: null }:
                // Inside a GOSUB this comes back to the call site. Outside
                // one it leaves the procedure — and at the top level there is
                // no procedure to leave, so it ends the program, which is
                // what QuickBASIC does and what main must return a value for.
                if (_inMain)
                {
                    if (_gosubSites > 0 || _program.Main.Any(HasGosub))
                        Line("if (qb_gosub_depth > 0) goto qb_gosub_dispatch;");

                    Line("return 0;");
                }
                else
                {
                    Line("return;");
                }

                break;

            case ReturnStatement returnStatement:
                Line($"return {Value(returnStatement.Value!)};");
                break;

            case ExitStatement exit:
                Line(exit.What is "FOR" or "DO" or "WHILE" ? "break;" : "return;");
                break;

            case EndStatement:
                Line("exit(0);");
                break;
        }
    }

    private void WriteAssignment(Assignment assignment)
    {
        var target = assignment.Target switch
        {
            VariableReference variable => Mangle(variable.Name),
            IndexOrCall indexed => $"{Mangle(indexed.Name)}[{Subscript(indexed.Arguments[0])}]",
            _ => "/* unsupported target */ *(int *)0"
        };

        Line($"{target} = {Converted(assignment.Value, TargetType(assignment.Target))};");
    }

    /// <summary>The type the thing being assigned to holds.</summary>
    private BasicType TargetType(Expression target) => target switch
    {
        VariableReference variable => _symbols.TypeOf(variable.Name),
        IndexOrCall indexed => _symbols.TypeOf(indexed.Name),
        _ => BasicType.Double
    };

    /// <summary>
    /// A value as the type it is being put into takes it.
    ///
    /// QuickBASIC rounds to the nearest whole number when a value goes into an
    /// INTEGER or a LONG, so 3.7 becomes 4. A plain C assignment truncates it
    /// to 3, which is a different program. Found by running the same source
    /// interpreted and compiled and comparing what they printed.
    /// </summary>
    private string Converted(Expression value, BasicType target)
    {
        var written = Value(value);

        if (target is not (BasicType.Integer or BasicType.Long)) return written;

        // A literal that is already whole needs nothing doing to it.
        if (value is NumberLiteral { Value: var number } && number == Math.Floor(number))
            return written;

        return $"({CType(target)})round({written})";
    }

    private void WritePrint(PrintStatement print)
    {
        foreach (var value in print.Values)
        {
            Line(TypeOf(value) == BasicType.String
                ? $"qb_print_string({Value(value)});"
                : $"qb_print_number({Value(value)});");
        }

        // A trailing semicolon holds the line open for the next PRINT.
        if (!print.TrailingSemicolon) Line("putchar('\\n');");
    }

    private void WriteInput(InputStatement input)
    {
        if (input.Prompt is { Length: > 0 } prompt)
            Line($"printf(\"%s\", {Quote(prompt)});");

        foreach (var target in input.Targets)
        {
            Line(TypeSuffix.Of(target) == BasicType.String
                ? $"{Mangle(target)} = qb_input_string();"
                : $"{Mangle(target)} = ({CType(TypeSuffix.Of(target))})qb_input_number();");
        }
    }

    private void WriteIf(IfStatement statement)
    {
        Line($"if ({Condition(statement.Condition)})");
        WriteBlock(statement.Then);

        foreach (var (condition, body) in statement.ElseIfs)
        {
            Line($"else if ({Condition(condition)})");
            WriteBlock(body);
        }

        if (statement.Else is { } otherwise)
        {
            Line("else");
            WriteBlock(otherwise);
        }
    }

    private void WriteFor(ForStatement statement)
    {
        var variable = Mangle(statement.Variable);
        var step = statement.Step is null ? "1" : Value(statement.Step);

        // The limit is read once into a temporary: QuickBASIC evaluates it at
        // the start, so changing it inside the loop must not move the end.
        var limit = $"qb_limit_{_labelCounter++}";

        Line($"{{ double {limit} = {Value(statement.To)};");
        _indent++;

        // The step's sign decides the comparison, and a negative step is only
        // known at run time when it is an expression.
        Line($"for ({variable} = {Value(statement.From)}; "
           + $"({step}) >= 0 ? {variable} <= {limit} : {variable} >= {limit}; "
           + $"{variable} += {step})");

        WriteBlock(statement.Body);

        _indent--;
        Line("}");
    }

    private void WriteDo(DoStatement statement)
    {
        if (statement.Condition is null)
        {
            Line("for (;;)");
            WriteBlock(statement.Body);
            return;
        }

        var test = statement.Until
            ? $"!({Condition(statement.Condition)})"
            : Condition(statement.Condition);

        if (statement.TestAtEnd)
        {
            Line("do");
            WriteBlock(statement.Body);
            Line($"while ({test});");
            return;
        }

        Line($"while ({test})");
        WriteBlock(statement.Body);
    }

    private void WriteSelect(SelectStatement statement)
    {
        var subject = $"qb_select_{_labelCounter++}";
        var type = TypeOf(statement.Value);

        Line($"{{ {CType(type)} {subject} = {Value(statement.Value)};");
        _indent++;

        var first = true;

        foreach (var (values, body) in statement.Cases)
        {
            var test = string.Join(" || ", values.Select(v => type == BasicType.String
                ? $"qb_str_compare({subject}, {Value(v)}) == 0"
                : $"{subject} == {Value(v)}"));

            Line($"{(first ? "if" : "else if")} ({test})");
            WriteBlock(body);

            first = false;
        }

        if (statement.Otherwise is { } otherwise)
        {
            Line(first ? "if (1)" : "else");
            WriteBlock(otherwise);
        }

        _indent--;
        Line("}");
    }

    private void WriteBlock(IReadOnlyList<Statement> body)
    {
        Line("{");
        _indent++;

        foreach (var statement in body) WriteStatement(statement);

        _indent--;
        Line("}");
    }

    /// <summary>
    /// An expression used as a condition.
    ///
    /// QuickBASIC has no boolean type: a comparison yields -1 for true and 0
    /// for false, and any non-zero value is true.
    /// </summary>
    private string Condition(Expression expression) => $"({Value(expression)}) != 0";

    private string Value(Expression expression) => expression switch
    {
        NumberLiteral number => number.Value == Math.Floor(number.Value)
                             && Math.Abs(number.Value) < long.MaxValue
            ? ((long)number.Value).ToString(CultureInfo.InvariantCulture)
            : number.Value.ToString("R", CultureInfo.InvariantCulture),

        StringLiteral text => $"qb_str_lit({Quote(text.Value)})",

        VariableReference variable => Mangle(variable.Name),

        IndexOrCall call => _symbols.IsArray(call.Name)
            ? $"{Mangle(call.Name)}[{Subscript(call.Arguments[0])}]"
            : LibraryCall(call) ?? $"{Mangle(call.Name)}({string.Join(", ", call.Arguments.Select(Value))})",

        Unary { Operator: "NOT" } unary => $"(~({Value(unary.Operand)}))",
        Unary unary => $"({unary.Operator}{Value(unary.Operand)})",

        Binary binary => BinaryValue(binary),

        _ => "0"
    };

    private string BinaryValue(Binary binary)
    {
        var left = Value(binary.Left);
        var right = Value(binary.Right);

        // Text compares and joins differently from numbers.
        if (TypeOf(binary.Left) == BasicType.String || TypeOf(binary.Right) == BasicType.String)
        {
            return binary.Operator switch
            {
                "+" => $"qb_str_concat({left}, {right})",
                "=" => $"(qb_str_compare({left}, {right}) == 0 ? -1 : 0)",
                "<>" => $"(qb_str_compare({left}, {right}) != 0 ? -1 : 0)",
                "<" => $"(qb_str_compare({left}, {right}) < 0 ? -1 : 0)",
                ">" => $"(qb_str_compare({left}, {right}) > 0 ? -1 : 0)",
                "<=" => $"(qb_str_compare({left}, {right}) <= 0 ? -1 : 0)",
                ">=" => $"(qb_str_compare({left}, {right}) >= 0 ? -1 : 0)",
                _ => "0"
            };
        }

        // A comparison yields -1 for true, which is what QuickBASIC does and
        // what makes "IF a = b" work with the bitwise operators below.
        return binary.Operator switch
        {
            "=" => $"(({left}) == ({right}) ? -1 : 0)",
            "<>" => $"(({left}) != ({right}) ? -1 : 0)",
            "<" => $"(({left}) < ({right}) ? -1 : 0)",
            ">" => $"(({left}) > ({right}) ? -1 : 0)",
            "<=" => $"(({left}) <= ({right}) ? -1 : 0)",
            ">=" => $"(({left}) >= ({right}) ? -1 : 0)",

            // AND, OR and XOR are bitwise in QuickBASIC, which is why true is
            // all ones rather than one.
            "AND" => $"(({left}) & ({right}))",
            "OR" => $"(({left}) | ({right}))",
            "XOR" => $"(({left}) ^ ({right}))",

            "MOD" => $"((long long)({left}) % (long long)({right}))",
            "\\" => $"((long long)({left}) / (long long)({right}))",

            // QuickBASIC's / always divides as a real number: 1 / 3 is
            // 0.333333, not zero. Without the cast C divides two integers as
            // integers, which is a different answer to the same program.
            "/" => $"((double)({left}) / (double)({right}))",
            "^" => $"pow({left}, {right})",

            _ => $"(({left}) {binary.Operator} ({right}))"
        };
    }

    /// <summary>What type an expression produces, as far as it can be told.</summary>
    private BasicType TypeOf(Expression expression) => expression switch
    {
        NumberLiteral number => number.Type,
        StringLiteral => BasicType.String,
        VariableReference variable => _symbols.TypeOf(variable.Name),
        IndexOrCall call => _symbols.TypeOf(call.Name),
        Unary unary => TypeOf(unary.Operand),

        // A comparison yields a number even when comparing text.
        Binary { Operator: "=" or "<>" or "<" or ">" or "<=" or ">=" } => BasicType.Integer,
        Binary binary => TypeOf(binary.Left) == BasicType.String
                      || TypeOf(binary.Right) == BasicType.String
            ? BasicType.String
            : BasicType.Double,

        _ => BasicType.Double
    };

    /// <summary>
    /// A call to one of the built-in functions, or null when it is not one.
    ///
    /// The names are the same ones the interpreter answers to, so a program
    /// using them runs either way.
    /// </summary>
    private string? LibraryCall(IndexOrCall call)
    {
        string Argument(int index) =>
            index < call.Arguments.Count ? Value(call.Arguments[index]) : "0";

        string Number(int index) => $"(int)({Argument(index)})";

        return call.Name.ToUpperInvariant() switch
        {
            "LEN" => $"qb_len({Argument(0)})",

            // MID$ with no count takes the rest, which -1 stands for.
            "MID$" => call.Arguments.Count > 2
                ? $"qb_mid({Argument(0)}, {Number(1)}, {Number(2)})"
                : $"qb_mid({Argument(0)}, {Number(1)}, -1)",

            "LEFT$" => $"qb_left({Argument(0)}, {Number(1)})",
            "RIGHT$" => $"qb_right({Argument(0)}, {Number(1)})",
            "CHR$" => $"qb_chr({Number(0)})",
            "ASC" => $"qb_asc({Argument(0)})",
            "VAL" => $"qb_val({Argument(0)})",
            "STR$" => $"qb_str({Argument(0)})",
            "UCASE$" => $"qb_case({Argument(0)}, 1)",
            "LCASE$" => $"qb_case({Argument(0)}, 0)",
            "LTRIM$" => $"qb_trim({Argument(0)}, 1)",
            "RTRIM$" => $"qb_trim({Argument(0)}, 0)",
            "SPACE$" => $"qb_space({Number(0)})",
            "INSTR" => $"qb_instr({Argument(0)}, {Argument(1)})",

            "ABS" => $"fabs({Argument(0)})",
            "INT" => $"floor({Argument(0)})",
            "FIX" => $"trunc({Argument(0)})",
            "SQR" => $"sqrt({Argument(0)})",
            "SGN" => $"(double)(({Argument(0)}) > 0 ? 1 : ({Argument(0)}) < 0 ? -1 : 0)",
            "CINT" => $"(double)(int)round({Argument(0)})",

            _ => null
        };
    }

    /// <summary>
    /// An array index, as C will take it.
    ///
    /// QuickBASIC variables are SINGLE unless a suffix says otherwise, so a
    /// plain loop counter is a float, and C refuses a float subscript. The
    /// cast is what QuickBASIC does anyway: an index is a whole number.
    /// </summary>
    private string Subscript(Expression index) => $"(int)({Value(index)})";

    private static string CType(BasicType type) => type switch
    {
        BasicType.Integer => "int",
        BasicType.Long => "long long",
        BasicType.Single => "float",
        BasicType.Double => "double",
        BasicType.String => "qb_string",
        _ => "void"
    };

    private static string ZeroOf(BasicType type) =>
        type == BasicType.String ? "qb_str_lit(\"\")" : "0";

    private static string Zero(VariableInfo variable) =>
        variable.IsArray
            ? "{0}"
            : ZeroOf(variable.Type);

    private static string ArraySuffix(VariableInfo variable) =>
        variable.IsArray ? $"[{variable.Length}]" : "";

    /// <summary>
    /// Turns a QuickBASIC name into a C one.
    ///
    /// The type suffixes are not valid in C, and "n%" and "n$" are different
    /// variables, so the suffix has to be encoded rather than dropped.
    /// </summary>
    public static string Mangle(string name)
    {
        var suffix = name.Length > 0 && name[^1] is '$' or '%' or '&' or '!' or '#'
            ? name[^1] switch
            {
                '$' => "_s",
                '%' => "_i",
                '&' => "_l",
                '!' => "_f",
                _ => "_d"
            }
            : "";

        var bare = suffix.Length > 0 ? name[..^1] : name;

        return "qb_" + bare.ToLowerInvariant() + suffix;
    }

    /// <summary>Writes a string as a C literal.</summary>
    private static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");

        foreach (var c in text)
        {
            quoted.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString()
            });
        }

        return quoted.Append('"').ToString();
    }

    private void Line(string text)
    {
        if (text.Length > 0) _output.Append(new string(' ', _indent * 4));

        _output.AppendLine(text);
    }
}
