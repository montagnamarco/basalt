using Basalt.Core.Localization;
using Basalt.Core.Services;

namespace Basalt.Workspace.Immediate;

/// <summary>What the Immediate window needs to know of the debug session.</summary>
public interface IImmediateDebugger
{
    /// <summary>Whether a program is being debugged at all.</summary>
    bool IsDebugging { get; }

    /// <summary>Whether it is stopped, so that there is a frame to evaluate in.</summary>
    bool IsPaused { get; }

    Task<ReplEvaluation> EvaluateAsync(string text, bool isExpression, CancellationToken ct = default);
}

/// <summary>
/// The Immediate window's brain: what a typed line goes to, and what is kept.
///
/// Stopped at a break, the line is the debugger's to evaluate, in the frame
/// being looked at. While the program runs there is nothing to ask, and the
/// answer is a polite refusal rather than a silent compile of something the
/// user did not mean. With no session at all, the line is compiled and run in
/// a separate process.
///
/// Every evaluation is started on the thread pool: the caller is the
/// interface thread, and compiling, building and waiting for an adapter are
/// all things it must not do. The result comes back to whoever awaited, which
/// is the interface thread again when the caller awaits in its own context.
/// </summary>
public sealed class ImmediateSession
{
    private readonly IImmediateDebugger? _debugger;
    private readonly IImmediateEvaluator _designTime;

    public ImmediateSession(IImmediateDebugger? debugger, IImmediateEvaluator designTime)
    {
        _debugger = debugger;
        _designTime = designTime;
    }

    public ImmediateHistory History { get; } = new();

    public Task<ImmediateOutcome> SubmitAsync(string line, CancellationToken ct = default)
    {
        var input = ImmediateInput.Parse(line);

        if (input.Kind == ImmediateKind.Empty)
            return Task.FromResult(new ImmediateOutcome(ImmediateOutcomeKind.Empty, ""));

        History.Add(line);

        // On the pool whichever way it goes: see the type's remarks.
        return Task.Run(() => EvaluateAsync(input, ct), ct);
    }

    private async Task<ImmediateOutcome> EvaluateAsync(ImmediateInput input, CancellationToken ct)
    {
        if (_debugger is { IsDebugging: true })
        {
            if (!_debugger.IsPaused)
            {
                return new ImmediateOutcome(
                    ImmediateOutcomeKind.Refused, Localizer.Get(StringKeys.ImmediateRunning));
            }

            // Said before asking: the debugger evaluates values, and running a
            // Visual Basic statement through an evaluator that speaks C# would
            // change what the line means.
            if (input.Kind == ImmediateKind.Statement)
            {
                return new ImmediateOutcome(
                    ImmediateOutcomeKind.Refused, Localizer.Get(StringKeys.ImmediateNoStatements));
            }

            var answer = await _debugger
                .EvaluateAsync(input.Text, input.Kind == ImmediateKind.Print, ct)
                .ConfigureAwait(false);

            if (answer.Succeeded)
                return new ImmediateOutcome(ImmediateOutcomeKind.Output, answer.Text);

            return new ImmediateOutcome(
                ImmediateOutcomeKind.RuntimeError,
                answer.Text.Length > 0 ? answer.Text : Localizer.Get(StringKeys.ImmediateNoValue));
        }

        return await _designTime.EvaluateAsync(input, ct).ConfigureAwait(false);
    }
}
