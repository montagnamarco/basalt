namespace Basalt.QuickBasic;

/// <summary>The type a value has.</summary>
public enum BasicType { Integer, Long, Single, Double, String, Void }

/// <summary>Anything in the tree, with where it was written.</summary>
public abstract record Node(int Line, int Column);

// Expressions

public abstract record Expression(int Line, int Column) : Node(Line, Column);

public sealed record NumberLiteral(double Value, BasicType Type, int Line, int Column)
    : Expression(Line, Column);

public sealed record StringLiteral(string Value, int Line, int Column)
    : Expression(Line, Column);

public sealed record VariableReference(string Name, int Line, int Column)
    : Expression(Line, Column);

/// <summary>An array element, or a call: QuickBASIC writes both with parentheses.</summary>
public sealed record IndexOrCall(
    string Name, IReadOnlyList<Expression> Arguments, int Line, int Column)
    : Expression(Line, Column);

public sealed record Unary(string Operator, Expression Operand, int Line, int Column)
    : Expression(Line, Column);

public sealed record Binary(
    string Operator, Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

// Statements

public abstract record Statement(int Line, int Column) : Node(Line, Column);

public sealed record Assignment(Expression Target, Expression Value, int Line, int Column)
    : Statement(Line, Column);

/// <summary>A declaration; <paramref name="Bounds"/> is set for an array.</summary>
public sealed record Declaration(
    string Name, BasicType Type, IReadOnlyList<Expression>? Bounds, int Line, int Column)
    : Statement(Line, Column);

public sealed record PrintStatement(
    IReadOnlyList<Expression> Values, bool TrailingSemicolon, int Line, int Column)
    : Statement(Line, Column);

public sealed record InputStatement(
    string? Prompt, IReadOnlyList<string> Targets, int Line, int Column)
    : Statement(Line, Column);

public sealed record IfStatement(
    Expression Condition,
    IReadOnlyList<Statement> Then,
    IReadOnlyList<(Expression Condition, IReadOnlyList<Statement> Body)> ElseIfs,
    IReadOnlyList<Statement>? Else,
    int Line, int Column)
    : Statement(Line, Column);

public sealed record ForStatement(
    string Variable, Expression From, Expression To, Expression? Step,
    IReadOnlyList<Statement> Body, int Line, int Column)
    : Statement(Line, Column);

public sealed record WhileStatement(
    Expression Condition, IReadOnlyList<Statement> Body, int Line, int Column)
    : Statement(Line, Column);

/// <summary>A DO loop; the test may be at either end and may be UNTIL.</summary>
public sealed record DoStatement(
    Expression? Condition, bool TestAtEnd, bool Until,
    IReadOnlyList<Statement> Body, int Line, int Column)
    : Statement(Line, Column);

public sealed record SelectStatement(
    Expression Value,
    IReadOnlyList<(IReadOnlyList<Expression> Values, IReadOnlyList<Statement> Body)> Cases,
    IReadOnlyList<Statement>? Otherwise,
    int Line, int Column)
    : Statement(Line, Column);

public sealed record GotoStatement(string Label, int Line, int Column) : Statement(Line, Column);

/// <summary>
/// GOSUB: like GOTO, but RETURN comes back here.
///
/// Its own node rather than a GOTO, because where to come back to is the
/// whole difference between the two.
/// </summary>
public sealed record GosubStatement(string Label, int Line, int Column) : Statement(Line, Column);

public sealed record LabelStatement(string Name, int Line, int Column) : Statement(Line, Column);

public sealed record CallStatement(
    string Name, IReadOnlyList<Expression> Arguments, int Line, int Column)
    : Statement(Line, Column);

public sealed record ReturnStatement(Expression? Value, int Line, int Column)
    : Statement(Line, Column);

public sealed record ExitStatement(string What, int Line, int Column) : Statement(Line, Column);

public sealed record EndStatement(int Line, int Column) : Statement(Line, Column);

// Procedures and the program

public sealed record Parameter(string Name, BasicType Type, bool IsArray);

public sealed record Procedure(
    string Name,
    IReadOnlyList<Parameter> Parameters,
    BasicType ReturnType,
    IReadOnlyList<Statement> Body,
    int Line, int Column)
    : Node(Line, Column)
{
    /// <summary>A FUNCTION returns a value; a SUB does not.</summary>
    public bool IsFunction => ReturnType != BasicType.Void;
}

/// <summary>A whole program: the statements at the top level and its procedures.</summary>
public sealed record Program(
    IReadOnlyList<Statement> Main,
    IReadOnlyList<Procedure> Procedures)
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
}

/// <summary>Something wrong with the program.</summary>
public sealed record Diagnostic(string Id, string Message, int Line, int Column)
{
    public override string ToString() => $"{Id} ({Line},{Column}): {Message}";
}

/// <summary>
/// How QuickBASIC decides a variable's type from its name.
///
/// The suffix is part of the name and says the type: "n%" is an integer and
/// "s$" a string. Without a suffix the type is single, as in the original.
/// </summary>
public static class TypeSuffix
{
    public static BasicType Of(string name) => name.Length == 0 ? BasicType.Single : name[^1] switch
    {
        '$' => BasicType.String,
        '%' => BasicType.Integer,
        '&' => BasicType.Long,
        '!' => BasicType.Single,
        '#' => BasicType.Double,
        _ => BasicType.Single
    };

    /// <summary>Whether a type holds text rather than a number.</summary>
    public static bool IsString(BasicType type) => type == BasicType.String;

    public static BasicType FromKeyword(string keyword) => keyword.ToUpperInvariant() switch
    {
        "INTEGER" => BasicType.Integer,
        "LONG" => BasicType.Long,
        "SINGLE" => BasicType.Single,
        "DOUBLE" => BasicType.Double,
        "STRING" => BasicType.String,
        _ => BasicType.Single
    };
}
