namespace Basalt.Workspace.Immediate;

public enum ImmediateOutcomeKind
{
    /// <summary>The line was blank; nothing happened.</summary>
    Empty,

    /// <summary>The line ran; the text is what it printed or evaluated to (possibly nothing).</summary>
    Output,

    /// <summary>The line did not compile; one diagnostic per line of text.</summary>
    CompileError,

    /// <summary>The line threw, or the debugger could not evaluate it.</summary>
    RuntimeError,

    /// <summary>The line ran past its deadline and its process was stopped.</summary>
    TimedOut,

    /// <summary>The program is running, so there is no frame to evaluate in.</summary>
    Refused,

    /// <summary>The project could not be built for the line to refer to it.</summary>
    BuildFailed
}

/// <summary>What a line typed in the Immediate window came to.</summary>
public sealed record ImmediateOutcome(ImmediateOutcomeKind Kind, string Text, int? ChildProcessId = null)
{
    /// <summary>Whether the text should be shown as a problem.</summary>
    public bool IsProblem => Kind is not (ImmediateOutcomeKind.Empty or ImmediateOutcomeKind.Output);
}
