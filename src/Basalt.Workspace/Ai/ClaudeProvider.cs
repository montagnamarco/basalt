using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>
/// Claude, through Anthropic's messages API and the official SDK.
///
/// Claude is the assistant the tool-using chat is designed around, so this one
/// goes through the SDK (typed requests, streaming events, typed errors) where
/// the other two keep to plain HTTP.
/// </summary>
public sealed class ClaudeProvider : IAiChatProvider
{
    public AiVendor Vendor => AiVendor.Claude;

    public string DisplayName => "Claude";

    public string ApiKeyUrl => "https://console.anthropic.com/settings/keys";

    public IReadOnlyList<AiModel> Models { get; } =
    [
        new("claude-opus-5-5", "Claude Opus 5.5"),
        new("claude-sonnet-5-5", "Claude Sonnet 5.5"),
        new("claude-haiku-4-5", "Claude Haiku 4.5")
    ];

    public IAiChatBackend CreateBackend(string apiKey, string modelId) =>
        new ClaudeBackend(apiKey, modelId);
}

/// <summary>One assistant turn from Claude, streamed.</summary>
internal sealed class ClaudeBackend : IAiChatBackend
{
    /// <summary>The beta that enables <c>fallbacks: "default"</c>.</summary>
    internal const string FallbackBeta = "server-side-fallback-2026-07-01";

    /// <summary>
    /// Streaming makes a long answer safe, and a large limit keeps a file
    /// rewritten in a tool call from being cut off mid-input.
    /// </summary>
    internal const int MaxTokens = 64000;

    /// <summary>What an id the user typed that is not a known family may be limited to.</summary>
    internal const int ConservativeMaxTokens = 16000;

    /// <summary>
    /// 64000 for the families that allow at least that much; a model id typed
    /// by hand that matches none of them gets a limit every Claude model takes.
    /// </summary>
    internal static int MaxTokensFor(string model) =>
        new[] { "claude-opus-5", "claude-sonnet-5", "claude-fable", "claude-mythos", "claude-haiku-4-5",
                "claude-opus-4-5", "claude-opus-4-6", "claude-opus-4-7", "claude-opus-4-8",
                "claude-sonnet-4-5", "claude-sonnet-4-6" }
            .Any(family => model.StartsWith(family, StringComparison.Ordinal))
            ? MaxTokens
            : ConservativeMaxTokens;

    private readonly AnthropicClient _client;
    private readonly string _model;

    public ClaudeBackend(string apiKey, string model, HttpClient? http = null, string? baseUrl = null)
    {
        _model = model;

        _client = (http, baseUrl) switch
        {
            (null, null) => new AnthropicClient { ApiKey = apiKey },
            (_, null) => new AnthropicClient { ApiKey = apiKey, HttpClient = http },
            _ => new AnthropicClient { ApiKey = apiKey, HttpClient = http ?? new HttpClient(), BaseUrl = baseUrl }
        };
    }

    // What each family accepts. A model id typed by the user that matches none
    // of these is sent plainly, without parameters it may refuse.

    /// <summary>The models with an effort setting.</summary>
    internal static bool SupportsEffort(string model) =>
        new[] { "claude-opus-5", "claude-sonnet-5", "claude-fable", "claude-mythos", "claude-opus-4-5",
                "claude-opus-4-6", "claude-opus-4-7", "claude-opus-4-8", "claude-sonnet-4-6" }
            .Any(prefix => model.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>The models whose safety classifiers can decline, and that can be given a fallback.</summary>
    internal static bool SupportsFallback(string model) =>
        model.StartsWith("claude-opus-5", StringComparison.Ordinal)
        || model.StartsWith("claude-fable", StringComparison.Ordinal);

    /// <summary>Builds the request exactly as it is sent.</summary>
    internal MessageCreateParams BuildParams(AiTurnRequest request)
    {
        // The system text is the same on every request and the tools are
        // sorted: tools render first, then system, and a breakpoint on the
        // last system block caches both. What varies (the solution, the
        // active file) is in the user message, so on the models that sign
        // their thinking to the conversation nothing earlier ever changes.
        var system = new List<BetaTextBlockParam>
        {
            new() { Text = request.StableSystem, CacheControl = new BetaCacheControlEphemeral() }
        };

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = MaxTokensFor(_model),
            System = system,
            Messages = [.. request.Messages.Select(ToParam)]
        };

        if (request.Tools.Count > 0)
        {
            // Sorted by name: the same tools must render to the same bytes
            // every request, or the cache never hits. Forcing a tool is a 400
            // on the current models; auto works everywhere.
            parameters = parameters with
            {
                Tools = [.. request.Tools.OrderBy(t => t.Name, StringComparer.Ordinal).Select(ToTool)],
                ToolChoice = new BetaToolChoiceAuto()
            };
        }

        // Thinking is left to the model: always on and adaptive on the current
        // Opus, where disabling it is rejected. Effort is the control.
        if (SupportsEffort(_model))
        {
            parameters = parameters with
            {
                OutputConfig = new BetaOutputConfig
                {
                    Effort = request.Effort switch
                    {
                        AiEffort.Low => Effort.Low,
                        AiEffort.High => Effort.High,
                        _ => Effort.Medium
                    }
                }
            };
        }

        if (SupportsFallback(_model))
        {
            // A request the safety classifiers decline is answered by the
            // recommended model instead of coming back as a refusal.
            parameters = parameters with
            {
                Betas = [FallbackBeta],
                Fallbacks = new BetaFallbacksParam(new Default())
            };
        }

        return parameters;
    }

    private static BetaToolUnion ToTool(AiTool tool)
    {
        var properties = tool.InputSchema["properties"] is JsonObject declared
            ? declared.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value))
            : [];

        var required = tool.InputSchema["required"] is JsonArray list
            ? list.Select(r => r!.GetValue<string>()).ToList()
            : [];

        return new BetaTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = new() { Properties = properties, Required = required },

            // The input streams as it is written; the API then no longer
            // validates it, so every call is validated before it runs.
            EagerInputStreaming = true
        };
    }

    private static BetaMessageParam ToParam(AiMessage message)
    {
        if (message.Role == AiRole.Assistant && message.Native is List<BetaContentBlockParam> native)
            return new BetaMessageParam { Role = Role.Assistant, Content = native };

        var blocks = new List<BetaContentBlockParam>();

        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case AiTextPart { Text.Length: > 0 } text:
                    blocks.Add(new BetaTextBlockParam
                    {
                        Text = text.Text,
                        CacheControl = text.CacheBreakpoint ? new BetaCacheControlEphemeral() : null
                    });
                    break;

                case AiToolCallPart call:
                    blocks.Add(new BetaToolUseBlockParam { ID = call.Id, Name = call.Name, Input = ToInput(call.Input) });
                    break;

                case AiToolResultPart result:
                    blocks.Add(new BetaToolResultBlockParam
                    {
                        ToolUseID = result.CallId,
                        Content = result.Content,
                        IsError = result.IsError ? true : null
                    });
                    break;
            }
        }

        return new BetaMessageParam
        {
            Role = message.Role == AiRole.Assistant ? Role.Assistant : Role.User,
            Content = blocks
        };
    }

    private static Dictionary<string, JsonElement> ToInput(JsonObject input) =>
        input.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));

    public async IAsyncEnumerable<AiTurnEvent> StreamTurnAsync(
        AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var accumulator = new TurnAccumulator(_model);

        var events = _client.Beta.Messages.CreateStreaming(BuildParams(request), ct)
            .GetAsyncEnumerator(ct);

        try
        {
            while (true)
            {
                BetaRawMessageStreamEvent next;

                try
                {
                    if (!await events.MoveNextAsync().ConfigureAwait(false)) break;

                    next = events.Current;
                }
                catch (AnthropicApiException ex)
                {
                    // The body says what is wrong (an expired key, a model that
                    // does not exist) and the user can act on that.
                    throw new InvalidOperationException(
                        AiErrors.Describe(ex.StatusCode, ex.ResponseBody ?? ex.Message), ex);
                }
                catch (AnthropicIOException ex)
                {
                    throw new InvalidOperationException(ex.Message, ex);
                }

                if (accumulator.Apply(next) is { Length: > 0 } text)
                    yield return new AiTextDelta(text);
            }
        }
        finally
        {
            await events.DisposeAsync().ConfigureAwait(false);
        }

        yield return accumulator.Finish();
    }

    /// <summary>
    /// Assembles one turn out of the stream's events, from blocks as they
    /// close: the only point a tool call's arguments are complete.
    /// </summary>
    internal sealed class TurnAccumulator(string requestedModel)
    {
        private sealed class Block
        {
            public string Kind = "";
            public readonly StringBuilder Text = new();
            public string Signature = "";
            public string Id = "";
            public string Name = "";
            public string RedactedData = "";
            public JsonObject? Input;
            public string? InputError;
        }

        private readonly SortedDictionary<long, Block> _blocks = [];
        private readonly List<Block> _finished = [];
        private string? _servedBy;
        private bool _fallbackBlock;
        private AiStopReason _stop = AiStopReason.EndTurn;
        private string? _category;

        /// <summary>Applies an event; returns the text it carries, if any.</summary>
        public string? Apply(BetaRawMessageStreamEvent streamEvent)
        {
            if (streamEvent.TryPickStart(out var start))
            {
                _servedBy = start.Message.Model.Raw();
                return null;
            }

            if (streamEvent.TryPickContentBlockStart(out var opened))
            {
                var block = new Block();
                var content = opened.ContentBlock;

                if (content.TryPickBetaText(out var text))
                {
                    block.Kind = "text";
                    block.Text.Append(text.Text);
                    _blocks[opened.Index] = block;
                    return text.Text;
                }

                if (content.TryPickBetaThinking(out var thinking))
                {
                    block.Kind = "thinking";
                    block.Text.Append(thinking.Thinking);
                    block.Signature = thinking.Signature ?? "";
                }
                else if (content.TryPickBetaRedactedThinking(out var redacted))
                {
                    block.Kind = "redacted";
                    block.RedactedData = redacted.Data;
                }
                else if (content.TryPickBetaToolUse(out var use))
                {
                    block.Kind = "tool_use";
                    block.Id = use.ID;
                    block.Name = use.Name;
                }
                else if (content.TryPickBetaFallback(out _))
                {
                    block.Kind = "fallback";
                    _fallbackBlock = true;

                    // The model that declined was thinking and calling tools
                    // for a request it will not finish: those blocks must not
                    // be echoed to the one that continues.
                    _finished.RemoveAll(b => b.Kind is "thinking" or "redacted" or "tool_use");
                    foreach (var key in _blocks.Where(b => b.Value.Kind is "thinking" or "redacted" or "tool_use")
                                 .Select(b => b.Key).ToList())
                        _blocks.Remove(key);
                }
                else
                {
                    block.Kind = "other";
                }

                _blocks[opened.Index] = block;
                return null;
            }

            if (streamEvent.TryPickContentBlockDelta(out var delta))
            {
                if (!_blocks.TryGetValue(delta.Index, out var block)) return null;

                if (delta.Delta.TryPickText(out var text))
                {
                    block.Text.Append(text.Text);
                    return text.Text;
                }

                if (delta.Delta.TryPickThinking(out var thinking))
                    block.Text.Append(thinking.Thinking);
                else if (delta.Delta.TryPickSignature(out var signature))
                    block.Signature = signature.Signature;
                else if (delta.Delta.TryPickInputJson(out var json))
                    block.Text.Append(json.PartialJson);

                return null;
            }

            if (streamEvent.TryPickContentBlockStop(out var stopped))
            {
                if (!_blocks.Remove(stopped.Index, out var block)) return null;

                if (block.Kind == "tool_use") ParseInput(block);

                _finished.Add(block);
                return null;
            }

            if (streamEvent.TryPickDelta(out var messageDelta))
            {
                var reason = messageDelta.Delta.StopReason;

                if (reason is not null)
                {
                    _stop = reason.Value() switch
                    {
                        BetaStopReason.ToolUse => AiStopReason.ToolUse,
                        BetaStopReason.EndTurn => AiStopReason.EndTurn,
                        BetaStopReason.StopSequence => AiStopReason.EndTurn,
                        BetaStopReason.MaxTokens => AiStopReason.MaxTokens,
                        BetaStopReason.Refusal => AiStopReason.Refusal,
                        _ => AiStopReason.Other
                    };
                }

                if (messageDelta.Delta.StopDetails?.Category is { } category)
                    _category = category.Raw();
            }

            return null;
        }

        /// <summary>
        /// Arguments arrive as JSON fragments. With eager input streaming the
        /// API has not checked them and a tolerant parser may return a
        /// truncated object, so they are parsed strictly here.
        /// </summary>
        private static void ParseInput(Block block)
        {
            var (input, error) = AiSse.ParseArguments(block.Text.ToString());
            block.Input = input;
            block.InputError = error;
        }

        public AiTurnEnd Finish()
        {
            // Blocks that never closed (a cut stream) are dropped: half a tool
            // call is not something to act on or to send back.
            var native = new List<BetaContentBlockParam>();
            var parts = new List<AiPart>();

            foreach (var block in _finished)
            {
                switch (block.Kind)
                {
                    case "text" when block.Text.Length > 0:
                        native.Add(new BetaTextBlockParam { Text = block.Text.ToString() });
                        parts.Add(new AiTextPart(block.Text.ToString()));
                        break;

                    case "thinking":
                        // The signature must be sent back exactly as it came.
                        native.Add(new BetaThinkingBlockParam { Thinking = block.Text.ToString(), Signature = block.Signature });
                        break;

                    case "redacted":
                        native.Add(new BetaRedactedThinkingBlockParam { Data = block.RedactedData });
                        break;

                    case "tool_use":
                        native.Add(new BetaToolUseBlockParam
                        {
                            ID = block.Id,
                            Name = block.Name,
                            Input = ToInput(block.Input ?? [])
                        });
                        parts.Add(new AiToolCallPart(block.Id, block.Name, block.Input ?? [], block.InputError));
                        break;
                }
            }

            return new AiTurnEnd(
                new AiMessage(AiRole.Assistant, parts, native),
                _stop,
                _category,
                _servedBy,
                _fallbackBlock || ServedByDiffers(_servedBy, requestedModel));
        }

        /// <summary>
        /// Whether another model answered. A dated id ("claude-haiku-4-5-20251001")
        /// is the same model as its alias, so one that starts with the other is not a switch.
        /// </summary>
        internal static bool ServedByDiffers(string? served, string requested) =>
            served is { Length: > 0 }
            && !served.StartsWith(requested, StringComparison.Ordinal)
            && !requested.StartsWith(served, StringComparison.Ordinal);
    }

    public void Dispose() => _client.Dispose();
}
