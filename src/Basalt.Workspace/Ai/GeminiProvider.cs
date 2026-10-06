using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>Gemini, through Google's generative language API.</summary>
public sealed class GeminiProvider : IAiChatProvider
{
    public AiVendor Vendor => AiVendor.Gemini;

    public string DisplayName => "Gemini";

    public string ApiKeyUrl => "https://aistudio.google.com/apikey";

    public IReadOnlyList<AiModel> Models { get; } =
    [
        new("gemini-2.5-pro", "Gemini 2.5 Pro"),
        new("gemini-2.5-flash", "Gemini 2.5 Flash"),
        new("gemini-2.0-flash", "Gemini 2.0 Flash")
    ];

    public IAiChatBackend CreateBackend(string apiKey, string modelId) =>
        new GeminiBackend(apiKey, modelId);
}

/// <summary>One assistant turn from Gemini, with function declarations.</summary>
internal sealed class GeminiBackend : IAiChatBackend
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;

    public GeminiBackend(string apiKey, string model, HttpClient? http = null,
        string baseUrl = "https://generativelanguage.googleapis.com")
    {
        _apiKey = apiKey;
        _model = model;
        _baseUrl = baseUrl.TrimEnd('/');
        _http = http ?? new HttpClient();
    }

    /// <summary>
    /// Where to send the request: the model is part of the path, and the key
    /// travels in a header so it cannot end up in a log of URLs.
    /// </summary>
    internal string Endpoint =>
        $"{_baseUrl}/v1beta/models/{Uri.EscapeDataString(_model)}:streamGenerateContent?alt=sse";

    internal JsonObject BuildRequest(AiTurnRequest request)
    {
        var contents = new JsonArray();

        foreach (var message in request.Messages)
        {
            if (message.Role == AiRole.Assistant)
            {
                // What the model said, exactly as it said it: its thought
                // signatures are checked on the next call.
                if (message.Native is JsonArray native)
                {
                    contents.Add(new JsonObject { ["role"] = "model", ["parts"] = native.DeepClone() });
                    continue;
                }

                var parts = new JsonArray();

                if (message.Text.Length > 0) parts.Add(new JsonObject { ["text"] = message.Text });

                foreach (var call in message.ToolCalls)
                    parts.Add(new JsonObject
                    {
                        ["functionCall"] = new JsonObject { ["name"] = call.Name, ["args"] = call.Input.DeepClone() }
                    });

                contents.Add(new JsonObject { ["role"] = "model", ["parts"] = parts });
                continue;
            }

            var userParts = new JsonArray();

            foreach (var result in message.Parts.OfType<AiToolResultPart>())
                userParts.Add(new JsonObject
                {
                    ["functionResponse"] = new JsonObject
                    {
                        ["name"] = result.Name,
                        ["response"] = new JsonObject { [result.IsError ? "error" : "result"] = result.Content }
                    }
                });

            if (message.Text.Length > 0) userParts.Add(new JsonObject { ["text"] = message.Text });

            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = userParts });
        }

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = request.SystemText() } }
            },
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = request.MaxTokens }
        };

        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["functionDeclarations"] = new JsonArray([.. request.Tools
                        .OrderBy(t => t.Name, StringComparer.Ordinal)
                        .Select(t => (JsonNode)new JsonObject
                        {
                            ["name"] = t.Name,
                            ["description"] = t.Description,
                            ["parameters"] = ToGeminiSchema(t.InputSchema)
                        })])
                }
            };

            body["toolConfig"] = new JsonObject
            {
                ["functionCallingConfig"] = new JsonObject { ["mode"] = "AUTO" }
            };
        }

        return body;
    }

    /// <summary>Gemini names its schema types in capitals and takes a subset of JSON schema.</summary>
    internal static JsonNode ToGeminiSchema(JsonNode schema)
    {
        switch (schema)
        {
            case JsonObject obj:
                var converted = new JsonObject();

                foreach (var (key, value) in obj)
                {
                    converted[key] = key == "type" && value is JsonValue v && v.TryGetValue<string>(out var type)
                        ? type.ToUpperInvariant()
                        : value is null ? null : ToGeminiSchema(value);
                }

                return converted;

            case JsonArray array:
                return new JsonArray([.. array.Select(item => item is null ? null : ToGeminiSchema(item))]);

            default:
                return schema.DeepClone();
        }
    }

    public async IAsyncEnumerable<AiTurnEvent> StreamTurnAsync(
        AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Add("x-goog-api-key", _apiKey);

        using var content = new StringContent(BuildRequest(request).ToJsonString(), Encoding.UTF8, "application/json");
        message.Content = content;

        using var response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            throw new InvalidOperationException(AiErrors.Describe(response.StatusCode, error));
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var turn = new TurnReader(_model);

        await foreach (var chunk in AiSse.ReadAsync(reader, ct).ConfigureAwait(false))
            if (turn.Apply(chunk) is { Length: > 0 } text)
                yield return new AiTextDelta(text);

        yield return turn.Finish();
    }

    /// <summary>Reads a turn out of the stream's chunks.</summary>
    internal sealed class TurnReader(string model)
    {
        private readonly JsonArray _native = [];
        private readonly StringBuilder _text = new();
        private readonly List<AiToolCallPart> _calls = [];
        private string? _finish;
        private bool _blocked;

        public string? Apply(JsonObject chunk)
        {
            if (chunk["promptFeedback"]?["blockReason"] is not null) _blocked = true;

            var candidate = chunk["candidates"]?[0];

            if (candidate is null) return null;

            if (candidate["finishReason"] is JsonValue finish && finish.TryGetValue<string>(out var reason))
                _finish = reason;

            if (candidate["content"]?["parts"] is not JsonArray parts) return null;

            var shown = new StringBuilder();

            foreach (var part in parts)
            {
                if (part is not JsonObject obj) continue;

                _native.Add(obj.DeepClone());

                if (obj["functionCall"] is JsonObject call)
                {
                    var args = call["args"] as JsonObject ?? [];

                    // Older models give no id; one is made so a call and its
                    // result can be told apart in the history.
                    var id = call["id"]?.GetValue<string>() ?? $"call_{_calls.Count + 1}";

                    _calls.Add(new AiToolCallPart(id, call["name"]?.GetValue<string>() ?? "", args.DeepClone().AsObject()));
                    continue;
                }

                // A summary of the model's thinking is not part of the answer.
                if (obj["thought"] is JsonValue thought && thought.TryGetValue<bool>(out var isThought) && isThought)
                    continue;

                if (obj["text"] is JsonValue text && text.TryGetValue<string>(out var value))
                {
                    _text.Append(value);
                    shown.Append(value);
                }
            }

            return shown.Length > 0 ? shown.ToString() : null;
        }

        public AiTurnEnd Finish()
        {
            var parts = new List<AiPart>();

            if (_text.Length > 0) parts.Add(new AiTextPart(_text.ToString()));
            parts.AddRange(_calls);

            var stop = _blocked || _finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" or "RECITATION"
                ? AiStopReason.Refusal
                : _finish == "MAX_TOKENS" ? AiStopReason.MaxTokens
                : _calls.Count > 0 ? AiStopReason.ToolUse
                : _finish is "STOP" or null ? AiStopReason.EndTurn
                : AiStopReason.Other;

            return new AiTurnEnd(new AiMessage(AiRole.Assistant, parts, _native), stop, ModelUsed: model);
        }
    }

    public void Dispose() => _http.Dispose();
}
