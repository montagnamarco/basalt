namespace Basalt.QuickBasic.Interpretation;

/// <summary>
/// An array, as QuickBASIC has them.
///
/// Bounds are kept because DIM a(5) means six elements, counted from zero,
/// and a program that reads a(5) is right to expect one there.
/// </summary>
public sealed class BasicArray(BasicType type, int upperBound)
{
    private readonly BasicValue[] _values =
        [.. Enumerable.Repeat(BasicValue.Default(type), upperBound + 1)];

    public BasicType Type { get; } = type;

    public int UpperBound { get; } = upperBound;

    public int Length => _values.Length;

    public BasicValue this[int index]
    {
        get => Within(index) ? _values[index] : BasicValue.Default(Type);
        set { if (Within(index)) _values[index] = value; }
    }

    public bool Within(int index) => index >= 0 && index < _values.Length;
}

/// <summary>
/// The variables one call can see.
///
/// QuickBASIC has no nested scopes: a procedure sees its own variables and
/// the shared ones, and nothing in between. So a scope is a flat set of names
/// rather than a chain.
/// </summary>
public sealed class Scope(string name)
{
    /// <summary>Whose variables these are: a procedure, or the top level.</summary>
    public string Name { get; } = name;

    /// <summary>The line being run, kept so the call stack can show it.</summary>
    public int Line { get; set; }

    public Dictionary<string, BasicValue> Variables { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, BasicArray> Arrays { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where a by-reference parameter came from, so writing it writes back.
    ///
    /// QuickBASIC passes by reference by default, which programs of the era
    /// rely on for returning more than one value.
    /// </summary>
    public Dictionary<string, (Scope Scope, string Name)> ByReference { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}
