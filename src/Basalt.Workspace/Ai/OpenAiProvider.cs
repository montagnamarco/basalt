using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>ChatGPT, through OpenAI's chat completions API.</summary>
public sealed class OpenAiProvider : IAiProvider
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

    public IChatClient CreateClient(string apiKey, string modelId) =>
        new OpenAiChatClient(apiKey, modelId);
}

/// <summary>Talks to OpenAI's chat completions endpoint.</summary>
internal sealed class OpenAiChatClient : IChatClient
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly string _model;

    public OpenAiChatClient(string apiKey, string model, HttpClient? http = null)
    {
        _model = model;

        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
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
        using var response = await _http
            .PostAsJsonAsync(Endpoint, BuildRequest(messages, options), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            throw new InvalidOperationException(
                ClaudeChatClient.Describe(response.StatusCode, error));
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var payload = line["data: ".Length..];

            if (payload == "[DONE]") yield break;

            var text = TextOf(payload);

            if (text.Length > 0) yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }
    }

    /// <summary>The text carried by one event, or empty when it carries none.</summary>
    internal static string TextOf(string payload)
    {
        try
        {
            return JsonNode.Parse(payload)?["choices"]?[0]?["delta"]?["content"]
                ?.GetValue<string>() ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    internal JsonObject BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var conversation = new JsonArray();

        // Here the system prompt is a message like any other, unlike Claude.
        foreach (var message in messages)
        {
            conversation.Add(new JsonObject
            {
                ["role"] = message.Role == ChatRole.Assistant ? "assistant"
                         : message.Role == ChatRole.System ? "system"
                         : "user",
                ["content"] = message.Text
            });
        }

        var request = new JsonObject
        {
            ["model"] = options?.ModelId ?? _model,
            ["stream"] = true,
            ["messages"] = conversation
        };

        if (options?.MaxOutputTokens is { } limit) request["max_completion_tokens"] = limit;
        if (options?.Temperature is { } temperature) request["temperature"] = temperature;

        return request;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => _http.Dispose();
}
