namespace Basalt.Core.Services;

/// <summary>
/// What the debugger answered to a line typed in the Immediate window.
///
/// A failed evaluation is data, not an exception: "name 'x' does not exist in
/// the current context" is the ordinary answer to a typo and belongs in the
/// transcript next to the line that caused it.
/// </summary>
public sealed record ReplEvaluation(bool Succeeded, string Text);
