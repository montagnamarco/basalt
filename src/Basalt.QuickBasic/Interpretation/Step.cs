namespace Basalt.QuickBasic.Interpretation;

/// <summary>
/// One thing the interpreter does, in a flat list.
///
/// The tree is turned into these before running, because an interpreter that
/// walks the tree by recursing cannot be stopped in the middle of an IF and
/// resumed afterwards — and stopping in the middle is what stepping is. As a
/// flat list, where the program is amounts to one number.
/// </summary>
public abstract record Step(int Line)
{
    /// <summary>Run a statement that does not move us elsewhere.</summary>
    public sealed record Execute(Statement Statement, int Line) : Step(Line);

    /// <summary>Go to another step, for a loop or a GOTO.</summary>
    public sealed record Jump(int Target, int Line) : Step(Line);

    /// <summary>Go elsewhere unless the condition holds.</summary>
    public sealed record JumpUnless(Expression Condition, int Target, int Line) : Step(Line);

    /// <summary>Start a FOR loop; <paramref name="After"/> is where to go if it never runs.</summary>
    public sealed record ForInit(
        string Variable, Expression From, Expression To, Expression? Step_, int After, int Line)
        : Step(Line)
    {
        public Expression? Step => Step_;
    }

    /// <summary>The NEXT of a FOR loop; <paramref name="Body"/> is where to go round again.</summary>
    public sealed record ForNext(string Variable, int Body, int Line) : Step(Line);

    /// <summary>Go to another step, remembering where to come back to.</summary>
    public sealed record Gosub(int Target, int Line) : Step(Line);

    /// <summary>Stop the program.</summary>
    public sealed record End(int Line) : Step(Line);
}
