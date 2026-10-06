using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>ChatGPT, through OpenAI's chat completions API.</summary>
public sealed class OpenAiProvider : IAiChatProvider
{
    public AiVendor Vendor => AiVendor.ChatGpt;

    public string DisplayName => "ChatGPT";

    public string ApiKeyUrl => "https://platform.openai.com/api-keys";

    public IReadOnlyList<AiModel> Models { get; } =
    [
        new("gpt-5", "GPT-5"),
        new("gpt-5-mini", "GPT-5 mini"),
        new("gpt-4o", "GPT-4o")
    ];

    public IAiChatBackend CreateBackend(string apiKey, string modelId) =>
        new OpenAiBackend(apiKey, modelId);
}

/// <summary>One assistant turn from OpenAI, with function calling.</summary>
internal sealed class OpenAiBackend : IAiChatBackend
{
    private const string DefaultEndpoint = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _endpoint;

    public OpenAiBackend(string apiKey, string model, HttpClient? http = null, string? endpoint = null)
    {
        _model = model;
        _endpoint = endpoint ?? DefaultEndpoint;

        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    internal JsonObject BuildRequest(AiTurnRequest request)
    {
        var conversation = new JsonArray
        {
            // Here the system prompt is a message like any other.
            new JsonObject { ["role"] = "system", ["content"] = request.SystemText() }
        };

        foreach (var message in request.Messages)
        {
            if (message.Role == AiRole.User)
            {
                // Each tool result is a message of its own, answering one call.
                foreach (var result in message.Parts.OfType<AiToolResultPart>())
                    conversation.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = result.CallId,
                        ["content"] = result.IsError ? $"Error: {result.Content}" : result.Content
                    });

                if (message.Text.Length > 0)
                    conversation.Add(new JsonObject { ["role"] = "user", ["content"] = message.Text });

                continue;
            }

            var assistant = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = message.Text.Length > 0 ? message.Text : null
            };

            if (message.ToolCalls.Count > 0)
            {
                assistant["tool_calls"] = new JsonArray([.. message.ToolCalls.Select(call =>
                    (JsonNode)new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.Input.ToJsonString()
                        }
                    })]);
            }

            conversation.Add(assistant);
        }

        var body = new JsonObject
        {
            ["model"] = _model,
            ["stream"] = true,
            ["messages"] = conversation,
            ["max_completion_tokens"] = request.MaxTokens
        };

        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray([.. request.Tools
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (JsonNode)new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.InputSchema.DeepClone()
                    }
                })]);

            body["tool_choice"] = "auto";
        }

        // Only the reasoning models take it; sending it to the others is an error.
        if (_model.StartsWith("gpt-5", StringComparison.Ordinal))
            body["reasoning_effort"] = request.Effort switch
            {
                AiEffort.Low => "low",
                AiEffort.High => "high",
                _ => "medium"
            };

        return body;
    }

    public async IAsyncEnumerable<AiTurnEvent> StreamTurnAsync(
        AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = await AiSse.PostAsync(_http, _endpoint, BuildRequest(request), ct)
            .ConfigureAwait(false);

        var turn = new TurnReader(_model);

        await foreach (var chunk in AiSse.ReadAsync(reader, ct).ConfigureAwait(false))
            if (turn.Apply(chunk) is { Length: > 0 } text)
                yield return new AiTextDelta(text);

        yield return turn.Finish();
    }

    /// <summary>Reads a turn out of the stream's chunks.</summary>
    internal sealed class TurnReader(string model)
    {
        private readonly StringBuilder _text = new();
        private readonly SortedDictionary<int, (string Id, string Name, StringBuilder Arguments)> _calls = [];
        private string? _finish;
        private bool _refused;
        private string? _served;

        public string? Apply(JsonObject chunk)
        {
            _served ??= chunk["model"]?.GetValue<string>();

            var choice = chunk["choices"]?[0];

            if (choice is null) return null;

            if (choice["finish_reason"] is JsonValue finish && finish.TryGetValue<string>(out var reason))
                _finish = reason;

            var delta = choice["delta"];

            if (delta is null) return null;

            if (delta["refusal"] is JsonValue refusal && refusal.TryGetValue<string>(out var refused) && refused.Length > 0)
                _refused = true;

            if (delta["tool_calls"] is JsonArray calls)
            {
                foreach (var call in calls)
                {
                    var index = call?["index"]?.GetValue<int>() ?? 0;

                    if (!_calls.TryGetValue(index, out var entry))
                        entry = ("", "", new StringBuilder());

                    var id = call?["id"]?.GetValue<string>();
                    var name = call?["function"]?["name"]?.GetValue<string>();

                    _calls[index] = (id ?? entry.Id, name ?? entry.Name, entry.Arguments);

                    if (call?["function"]?["arguments"]?.GetValue<string>() is { } arguments)
                        entry.Arguments.Append(arguments);
                }
            }

            if (delta["content"] is JsonValue content && content.TryGetValue<string>(out var text) && text.Length > 0)
            {
                _text.Append(text);
                return text;
            }

            return null;
        }

        public AiTurnEnd Finish()
        {
            var parts = new List<AiPart>();

            if (_text.Length > 0) parts.Add(new AiTextPart(_text.ToString()));

            foreach (var (_, (id, name, arguments)) in _calls)
            {
                var (input, error) = AiSse.ParseArguments(arguments.ToString());
                parts.Add(new AiToolCallPart(id, name, input, error));
            }

            var stop = _refused || _finish == "content_filter" ? AiStopReason.Refusal
                : _finish == "length" ? AiStopReason.MaxTokens
                : _finish == "tool_calls" || _finish == "function_call" || _calls.Count > 0 ? AiStopReason.ToolUse
                : _finish == "stop" ? AiStopReason.EndTurn
                : AiStopReason.Other;

            return new AiTurnEnd(new AiMessage(AiRole.Assistant, parts), stop, ModelUsed: _served ?? model);
        }
    }

    public void Dispose() => _http.Dispose();
}
