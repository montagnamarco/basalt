namespace Basalt.QuickBasic;

/// <summary>What is known about a variable.</summary>
public sealed record VariableInfo(string Name, BasicType Type, bool IsArray, int Length);

/// <summary>
/// The variables and procedures a program declares, and what is wrong with it.
///
/// QuickBASIC declares variables by using them, so this both collects what
/// exists and reports what cannot be right: a call to a procedure that is not
/// there, an array indexed as a number, a type that cannot be assigned.
/// </summary>
public sealed class SymbolTable
{
    /// <summary>The size an array gets when its bound is not a literal.</summary>
    private const int DefaultArrayLength = 256;

    private readonly Dictionary<string, VariableInfo> _globals =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, VariableInfo>> _locals =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Procedure> _procedures =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<Diagnostic> _diagnostics = [];

    private SymbolTable() { }

    public IReadOnlyList<VariableInfo> Globals => [.. _globals.Values];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<VariableInfo> LocalsOf(string procedureName) =>
        _locals.TryGetValue(procedureName, out var locals) ? [.. locals.Values] : [];

    public bool IsGlobal(string name) => _globals.ContainsKey(name);

    public bool IsArray(string name) =>
        _globals.TryGetValue(name, out var global) && global.IsArray
        || _locals.Values.Any(scope => scope.TryGetValue(name, out var local) && local.IsArray);

    /// <summary>The type of a name, whether variable or function.</summary>
    public BasicType TypeOf(string name)
    {
        if (_procedures.TryGetValue(name, out var procedure) && procedure.IsFunction)
            return procedure.ReturnType;

        if (_globals.TryGetValue(name, out var global)) return global.Type;

        foreach (var scope in _locals.Values)
            if (scope.TryGetValue(name, out var local)) return local.Type;

        // A name never declared still has the type its suffix gives it.
        return TypeSuffix.Of(name);
    }

    /// <summary>Reads a program and checks it.</summary>
    public static SymbolTable Build(Program program)
    {
        var table = new SymbolTable();

        foreach (var procedure in program.Procedures)
        {
            if (!table._procedures.TryAdd(procedure.Name, procedure))
            {
                table.Report("QB020",
                    $"'{procedure.Name}' is declared more than once.",
                    procedure.Line, procedure.Column);
            }
        }

        foreach (var statement in program.Main) table.Collect(statement, scope: null);

        foreach (var procedure in program.Procedures)
        {
            var scope = new Dictionary<string, VariableInfo>(StringComparer.OrdinalIgnoreCase);
            table._locals[procedure.Name] = scope;

            foreach (var parameter in procedure.Parameters)
            {
                scope[parameter.Name] = new VariableInfo(
                    parameter.Name, parameter.Type, parameter.IsArray, DefaultArrayLength);
            }

            foreach (var statement in procedure.Body) table.Collect(statement, procedure.Name);
        }

        foreach (var statement in program.Main) table.Check(statement, scope: null);

        foreach (var procedure in program.Procedures)
            foreach (var statement in procedure.Body)
                table.Check(statement, procedure.Name);

        return table;
    }

    /// <summary>Records what a statement declares or uses.</summary>
    private void Collect(Statement statement, string? scope)
    {
        switch (statement)
        {
            case Declaration declaration:
                Declare(
                    declaration.Name,
                    declaration.Type,
                    declaration.Bounds is { Count: > 0 },
                    LengthOf(declaration.Bounds),
                    scope);
                break;

            case Assignment assignment:
                // Assigning to a name declares it, as QuickBASIC does.
                if (assignment.Target is VariableReference variable)
                    Declare(variable.Name, TypeSuffix.Of(variable.Name), false, 0, scope);

                CollectExpression(assignment.Value, scope);
                break;

            case InputStatement input:
                foreach (var target in input.Targets)
                    Declare(target, TypeSuffix.Of(target), false, 0, scope);
                break;

            case ForStatement forStatement:
                Declare(forStatement.Variable, TypeSuffix.Of(forStatement.Variable), false, 0, scope);
                foreach (var inner in forStatement.Body) Collect(inner, scope);
                break;

            case IfStatement ifStatement:
                foreach (var inner in ifStatement.Then) Collect(inner, scope);
                foreach (var (_, body) in ifStatement.ElseIfs)
                    foreach (var inner in body) Collect(inner, scope);
                if (ifStatement.Else is { } otherwise)
                    foreach (var inner in otherwise) Collect(inner, scope);
                break;

            case WhileStatement whileStatement:
                foreach (var inner in whileStatement.Body) Collect(inner, scope);
                break;

            case DoStatement doStatement:
                foreach (var inner in doStatement.Body) Collect(inner, scope);
                break;

            case SelectStatement select:
                foreach (var (_, body) in select.Cases)
                    foreach (var inner in body) Collect(inner, scope);
                if (select.Otherwise is { } fallback)
                    foreach (var inner in fallback) Collect(inner, scope);
                break;
        }
    }

    private void CollectExpression(Expression expression, string? scope)
    {
        // An identifier used before assignment still exists in QuickBASIC,
        // holding zero or the empty string.
        switch (expression)
        {
            case VariableReference variable:
                Declare(variable.Name, TypeSuffix.Of(variable.Name), false, 0, scope);
                break;

            case Binary binary:
                CollectExpression(binary.Left, scope);
                CollectExpression(binary.Right, scope);
                break;

            case Unary unary:
                CollectExpression(unary.Operand, scope);
                break;
        }
    }

    private void Declare(string name, BasicType type, bool isArray, int length, string? scope)
    {
        var target = scope is null
            ? _globals
            : _locals.TryGetValue(scope, out var locals) ? locals : _locals[scope] = [];

        if (target.TryGetValue(name, out var existing))
        {
            // A later DIM of the same name as an array wins: the first use may
            // have been a plain read that told us nothing.
            if (isArray && !existing.IsArray)
                target[name] = new VariableInfo(name, type, true, length);

            return;
        }

        target[name] = new VariableInfo(name, type, isArray, length);
    }

    private static int LengthOf(IReadOnlyList<Expression>? bounds)
    {
        if (bounds is not { Count: > 0 }) return 0;

        // Arrays are indexed from zero through the bound, so a bound of 10
        // needs eleven slots.
        return bounds[0] is NumberLiteral number
            ? (int)number.Value + 1
            : DefaultArrayLength;
    }

    /// <summary>Reports what cannot be right.</summary>
    private void Check(Statement statement, string? scope)
    {
        switch (statement)
        {
            case CallStatement call:
                CheckCall(call.Name, call.Arguments.Count, call.Line, call.Column);
                foreach (var argument in call.Arguments) CheckExpression(argument, scope);
                break;

            case Assignment assignment:
                CheckExpression(assignment.Value, scope);
                CheckAssignment(assignment, scope);
                break;

            case PrintStatement print:
                foreach (var value in print.Values) CheckExpression(value, scope);
                break;

            case IfStatement ifStatement:
                CheckExpression(ifStatement.Condition, scope);
                foreach (var inner in ifStatement.Then) Check(inner, scope);
                foreach (var (condition, body) in ifStatement.ElseIfs)
                {
                    CheckExpression(condition, scope);
                    foreach (var inner in body) Check(inner, scope);
                }
                if (ifStatement.Else is { } otherwise)
                    foreach (var inner in otherwise) Check(inner, scope);
                break;

            case ForStatement forStatement:
                CheckExpression(forStatement.From, scope);
                CheckExpression(forStatement.To, scope);
                foreach (var inner in forStatement.Body) Check(inner, scope);
                break;

            case WhileStatement whileStatement:
                CheckExpression(whileStatement.Condition, scope);
                foreach (var inner in whileStatement.Body) Check(inner, scope);
                break;

            case DoStatement doStatement:
                if (doStatement.Condition is { } test) CheckExpression(test, scope);
                foreach (var inner in doStatement.Body) Check(inner, scope);
                break;

            case SelectStatement select:
                CheckExpression(select.Value, scope);
                foreach (var (_, body) in select.Cases)
                    foreach (var inner in body) Check(inner, scope);
                if (select.Otherwise is { } fallback)
                    foreach (var inner in fallback) Check(inner, scope);
                break;
        }
    }

    private void CheckAssignment(Assignment assignment, string? scope)
    {
        if (assignment.Target is not VariableReference variable) return;

        var target = TypeOf(variable.Name);
        var value = TypeOfExpression(assignment.Value, scope);

        // Text and numbers do not mix, which is the one type error QuickBASIC
        // itself refuses to compile.
        if (TypeSuffix.IsString(target) != TypeSuffix.IsString(value))
        {
            Report("QB021",
                TypeSuffix.IsString(target)
                    ? $"Cannot assign a number to the string '{variable.Name}'."
                    : $"Cannot assign a string to the number '{variable.Name}'.",
                assignment.Line, assignment.Column);
        }
    }

    private void CheckExpression(Expression expression, string? scope)
    {
        switch (expression)
        {
            case IndexOrCall call when !IsArray(call.Name):
                CheckCall(call.Name, call.Arguments.Count, call.Line, call.Column);
                foreach (var argument in call.Arguments) CheckExpression(argument, scope);
                break;

            case IndexOrCall indexed:
                foreach (var argument in indexed.Arguments) CheckExpression(argument, scope);
                break;

            case Binary binary:
                CheckExpression(binary.Left, scope);
                CheckExpression(binary.Right, scope);
                CheckOperands(binary, scope);
                break;

            case Unary unary:
                CheckExpression(unary.Operand, scope);
                break;
        }
    }

    private void CheckOperands(Binary binary, string? scope)
    {
        var left = TypeOfExpression(binary.Left, scope);
        var right = TypeOfExpression(binary.Right, scope);

        if (TypeSuffix.IsString(left) == TypeSuffix.IsString(right)) return;

        Report("QB022",
            $"Cannot apply '{binary.Operator}' to a string and a number.",
            binary.Line, binary.Column);
    }

    private void CheckCall(string name, int argumentCount, int line, int column)
    {
        if (Builtins.Contains(name)) return;

        if (!_procedures.TryGetValue(name, out var procedure))
        {
            Report("QB023", $"'{name}' is not defined.", line, column);
            return;
        }

        if (argumentCount != procedure.Parameters.Count)
        {
            Report("QB024",
                $"'{name}' takes {procedure.Parameters.Count} argument(s), "
              + $"but {argumentCount} were given.",
                line, column);
        }
    }

    private BasicType TypeOfExpression(Expression expression, string? scope) => expression switch
    {
        NumberLiteral number => number.Type,
        StringLiteral => BasicType.String,
        VariableReference variable => TypeOf(variable.Name),
        IndexOrCall call => TypeOf(call.Name),
        Unary unary => TypeOfExpression(unary.Operand, scope),

        Binary { Operator: "=" or "<>" or "<" or ">" or "<=" or ">=" } => BasicType.Integer,

        Binary binary => TypeOfExpression(binary.Left, scope) == BasicType.String
            ? BasicType.String
            : TypeOfExpression(binary.Right, scope),

        _ => BasicType.Double
    };

    /// <summary>
    /// The procedures the runtime provides.
    ///
    /// Listed so a call to one is not reported as undefined; the code writer
    /// emits them directly.
    /// </summary>
    /// <summary>
    /// The names a program may use without defining them.
    ///
    /// The statements, and the library functions the interpreter and the code
    /// generator both answer to. All three have to agree on this list, or a
    /// program runs one way and not the other.
    /// </summary>
    private static readonly HashSet<string> Builtins =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PRINT", "INPUT", "CLS", "SLEEP",

            "LEN", "MID$", "LEFT$", "RIGHT$", "CHR$", "ASC", "VAL", "STR$",
            "ABS", "INT", "RND", "TIMER", "SQR", "SGN", "UCASE$", "LCASE$",
            "LTRIM$", "RTRIM$", "SPACE$", "STRING$", "INSTR", "FIX", "CINT"
        };

    private void Report(string id, string message, int line, int column) =>
        _diagnostics.Add(new Diagnostic(id, message, line, column));
}
