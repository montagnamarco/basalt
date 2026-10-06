using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Basalt.Core.Localization;

namespace Basalt.Workspace.Ai;

/// <summary>Something that happens while the assistant works on a question.</summary>
public abstract record AiChatEvent;

/// <summary>A piece of the reply, as it arrives.</summary>
public sealed record AiTextChunk(string Text) : AiChatEvent;

/// <summary>The assistant asked for a tool and it is about to run.</summary>
public sealed record AiToolStarted(AiToolCallPart Call) : AiChatEvent;

/// <summary>A tool has run.</summary>
public sealed record AiToolFinished(AiToolCallPart Call, AiToolResult Result) : AiChatEvent;

/// <summary>What kind of thing the IDE is telling the user about the reply.</summary>
public enum AiNoticeKind { FallbackModel, Refusal, Truncated, RoundLimit }

/// <summary>
/// The IDE speaking rather than the assistant: a fallback model answered, the
/// request was declined, the answer was cut off.
/// </summary>
public sealed record AiNotice(AiNoticeKind Kind, string Text) : AiChatEvent;

/// <summary>What the loop needs to ask for a turn, besides the conversation.</summary>
public sealed record AiLoopRequest(
    string StableSystem,
    IReadOnlyList<AiTool> Tools,
    AiEffort Effort = AiEffort.Medium,
    int MaxTokens = 16000);

/// <summary>
/// Runs a conversation turn to its end: the assistant speaks, asks for tools,
/// gets their results, and speaks again, until it has nothing more to ask.
///
/// The same loop serves every vendor; a backend only translates one turn.
/// </summary>
public static class AiToolLoop
{
    /// <summary>A guard against an assistant that keeps asking for tools forever.</summary>
    public const int MaxRounds = 25;

    /// <summary>Longest tool result sent back; a result is context the assistant pays for on every later turn.</summary>
    internal const int MaxResultChars = 40_000;

    /// <summary>
    /// Runs until the assistant stops, adding every message to <paramref name="history"/>.
    ///
    /// The history only ever ends at a point a vendor accepts: a tool call is
    /// added together with its results or not at all, so cancelling or a
    /// refusal in the middle of a round cannot leave a call unanswered.
    /// </summary>
    public static async IAsyncEnumerable<AiChatEvent> RunAsync(
        IAiChatBackend backend,
        AiLoopRequest request,
        List<AiMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var tools = request.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        for (var round = 0; round < MaxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            var turn = new AiTurnRequest(
                request.StableSystem,
                request.Tools, [.. history], request.Effort, request.MaxTokens);

            AiTurnEnd? end = null;

            await foreach (var update in backend.StreamTurnAsync(turn, ct).ConfigureAwait(false))
            {
                switch (update)
                {
                    case AiTextDelta delta:
                        yield return new AiTextChunk(delta.Text);
                        break;

                    case AiTurnEnd finished:
                        end = finished;
                        break;
                }
            }

            if (end is null)
                throw new InvalidOperationException("The assistant's reply ended without a result.");

            if (end.FellBack)
                yield return new AiNotice(AiNoticeKind.FallbackModel,
                    Localizer.Get(StringKeys.AiNoticeFallback, end.ModelUsed ?? "?"));

            var calls = end.Message.ToolCalls;

            // A turn that asks for tools is a tool turn whatever the vendor
            // called its stop; leaving the calls unanswered would make the
            // next request invalid.
            var stop = end.Stop is AiStopReason.EndTurn or AiStopReason.Other && calls.Count > 0
                ? AiStopReason.ToolUse
                : end.Stop;

            switch (stop)
            {
                case AiStopReason.Refusal:
                    // Checked before anything else, tools included: a
                    // refusal can cut a call off mid-input.
                    yield return new AiNotice(AiNoticeKind.Refusal,
                        end.StopCategory is { Length: > 0 } category
                            ? Localizer.Get(StringKeys.AiNoticeRefusalCategory, category)
                            : Localizer.Get(StringKeys.AiNoticeRefusal));
                    yield break;

                case AiStopReason.MaxTokens when calls.Count > 0:
                    // A truncated call usually parses as a valid partial
                    // object; running it would act on half an instruction.
                    yield return new AiNotice(AiNoticeKind.Truncated,
                        Localizer.Get(StringKeys.AiNoticeTruncatedTool));
                    yield break;

                case AiStopReason.MaxTokens:
                    history.Add(end.Message);
                    yield return new AiNotice(AiNoticeKind.Truncated,
                        Localizer.Get(StringKeys.AiNoticeTruncated));
                    yield break;
            }

            if (stop != AiStopReason.ToolUse || calls.Count == 0)
            {
                history.Add(end.Message);
                yield break;
            }

            var mark = history.Count;
            history.Add(end.Message);

            var results = new List<AiPart>();
            var completed = false;

            try
            {
                foreach (var call in calls)
                {
                    yield return new AiToolStarted(call);

                    var result = await ExecuteAsync(call, tools, ct).ConfigureAwait(false);

                    results.Add(new AiToolResultPart(call.Id, call.Name, result.Content, result.IsError));

                    yield return new AiToolFinished(call, result);
                }

                history.Add(new AiMessage(AiRole.User, results));
                completed = true;
            }
            finally
            {
                // Stopped or failed midway: take the unanswered calls back.
                if (!completed && mark <= history.Count) history.RemoveRange(mark, history.Count - mark);
            }
        }

        yield return new AiNotice(AiNoticeKind.RoundLimit,
            Localizer.Get(StringKeys.AiNoticeRoundLimit, MaxRounds));
    }

    private static async Task<AiToolResult> ExecuteAsync(
        AiToolCallPart call, Dictionary<string, AiTool> tools, CancellationToken ct)
    {
        if (!tools.TryGetValue(call.Name, out var tool))
            return AiToolResult.Error($"There is no tool named '{call.Name}'.");

        if (call.InputError is { } unreadable)
            return AiToolResult.Error($"The arguments could not be read: {unreadable}. Send them again as one complete JSON object.");

        if (tool.Validate(call.Input) is { } invalid)
            return AiToolResult.Error($"Invalid arguments: {invalid}");

        try
        {
            // Off the interface thread: a tool reads files and asks the
            // language service, and none of that belongs on it.
            var result = await Task.Run(() => tool.Handler(call.Input, ct), ct).ConfigureAwait(false);

            return result.Content.Length <= MaxResultChars
                ? result
                : result with { Content = AiText.Truncate(result.Content, MaxResultChars, "\n(output truncated)") };
        }
        catch (AiToolException ex)
        {
            return AiToolResult.Error(ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or InvalidOperationException or ArgumentException
                                      or FormatException or InvalidCastException
                                      or System.Text.Json.JsonException)
        {
            // Said to the assistant, not thrown: it can try something else.
            return AiToolResult.Error($"{call.Name} failed: {ex.Message}");
        }
    }
}
