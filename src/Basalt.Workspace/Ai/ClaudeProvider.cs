using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>
/// Claude, through Anthropic's messages API.
///
/// Spoken to directly rather than through a vendor SDK: the request is a JSON
/// body and the reply is a stream of events, and three SDKs would be a large
/// dependency surface for what fits in one readable file each.
/// </summary>
public sealed class ClaudeProvider : IAiProvider
{
    public AiVendor Vendor => AiVendor.Claude;

    public string DisplayName => "Claude";

    public string ApiKeyUrl => "https://console.anthropic.com/settings/keys";

    public IReadOnlyList<AiModel> Models { get; } =
    [
        new("claude-opus-4-5", "Claude Opus 4.5"),
        new("claude-sonnet-4-5", "Claude Sonnet 4.5"),
        new("claude-haiku-4-5", "Claude Haiku 4.5")
    ];

    public IChatClient CreateClient(string apiKey, string modelId) =>
        new ClaudeChatClient(apiKey, modelId);
}

/// <summary>Talks to Anthropic's messages endpoint.</summary>
internal sealed class ClaudeChatClient : IChatClient
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";

    /// <summary>What the API is asked to answer within; it refuses without one.</summary>
    private const int MaxTokens = 4096;

    private readonly HttpClient _http;
    private readonly string _model;

    public ClaudeChatClient(string apiKey, string model, HttpClient? http = null)
    {
        _model = model;

        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Remove("anthropic-version");
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();

        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
            text.Append(update.Text);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text.ToString()));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, options);

        using var response = await _http.PostAsJsonAsync(Endpoint, request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // The body says what is wrong — an expired key, a model that does
            // not exist — and the user can act on that.
            var error = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            throw new InvalidOperationException(Describe(response.StatusCode, error));
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var reader = new StreamReader(stream);

        await foreach (var text in ReadEventsAsync(reader, cancellationToken).ConfigureAwait(false))
            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
    }

    /// <summary>
    /// Reads the text out of a server-sent event stream.
    ///
    /// Only the deltas carry text; the other events describe the shape of the
    /// reply and are of no use here.
    /// </summary>
    internal static async IAsyncEnumerable<string> ReadEventsAsync(
        TextReader reader, [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var payload = line["data: ".Length..];

            if (payload == "[DONE]") yield break;

            var text = TextOf(payload);

            if (text.Length > 0) yield return text;
        }
    }

    /// <summary>The text carried by one event, or empty when it carries none.</summary>
    internal static string TextOf(string payload)
    {
        JsonNode? node;

        try
        {
            node = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return "";
        }

        return node?["delta"]?["text"]?.GetValue<string>() ?? "";
    }

    /// <summary>Turns the request into the shape the API expects.</summary>
    internal JsonObject BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var conversation = new JsonArray();
        var system = new StringBuilder();

        foreach (var message in messages)
        {
            // The system prompt is a field of its own here, not a message.
            if (message.Role == ChatRole.System)
            {
                if (system.Length > 0) system.Append('\n');
                system.Append(message.Text);
                continue;
            }

            conversation.Add(new JsonObject
            {
                ["role"] = message.Role == ChatRole.Assistant ? "assistant" : "user",
                ["content"] = message.Text
            });
        }

        var request = new JsonObject
        {
            ["model"] = options?.ModelId ?? _model,
            ["max_tokens"] = options?.MaxOutputTokens ?? MaxTokens,
            ["stream"] = true,
            ["messages"] = conversation
        };

        if (system.Length > 0) request["system"] = system.ToString();

        if (options?.Temperature is { } temperature) request["temperature"] = temperature;

        return request;
    }

    /// <summary>Turns a failed request into something worth reading.</summary>
    internal static string Describe(System.Net.HttpStatusCode status, string body)
    {
        var message = body;

        try
        {
            if (JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() is { } detail)
                message = detail;
        }
        catch (JsonException)
        {
            // The body was not JSON; it is still the best thing to show.
        }

        return status switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "The API key was refused. Check it in Settings.",
            System.Net.HttpStatusCode.TooManyRequests =>
                "The assistant is rate limiting: wait a moment and try again.",
            _ => $"The assistant returned {(int)status}: {message}"
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => _http.Dispose();
}
