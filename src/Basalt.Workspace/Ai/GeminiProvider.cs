using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>Gemini, through Google's generative language API.</summary>
public sealed class GeminiProvider : IAiProvider
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

    public IChatClient CreateClient(string apiKey, string modelId) =>
        new GeminiChatClient(apiKey, modelId);
}

/// <summary>Talks to Google's generative language endpoint.</summary>
internal sealed class GeminiChatClient : IChatClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiChatClient(string apiKey, string model, HttpClient? http = null)
    {
        _apiKey = apiKey;
        _model = model;
        _http = http ?? new HttpClient();
    }

    /// <summary>
    /// Where to send the request.
    ///
    /// The model is part of the path here rather than of the body, and the key
    /// is a query parameter rather than a header.
    /// </summary>
    internal string EndpointFor(string model) =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{model}"
      + $":streamGenerateContent?alt=sse&key={_apiKey}";

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
        var endpoint = EndpointFor(options?.ModelId ?? _model);

        using var response = await _http
            .PostAsJsonAsync(endpoint, BuildRequest(messages, options), cancellationToken)
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

            var text = TextOf(line["data: ".Length..]);

            if (text.Length > 0) yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }
    }

    /// <summary>The text carried by one event, or empty when it carries none.</summary>
    internal static string TextOf(string payload)
    {
        try
        {
            return JsonNode.Parse(payload)
                ?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]
                ?.GetValue<string>() ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    internal JsonObject BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var contents = new JsonArray();
        var system = new StringBuilder();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (system.Length > 0) system.Append('\n');
                system.Append(message.Text);
                continue;
            }

            contents.Add(new JsonObject
            {
                // "model" rather than "assistant": the same idea, a different
                // word, which is exactly the sort of thing this layer hides.
                ["role"] = message.Role == ChatRole.Assistant ? "model" : "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = message.Text } }
            });
        }

        var request = new JsonObject { ["contents"] = contents };

        if (system.Length > 0)
        {
            request["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = system.ToString() } }
            };
        }

        var configuration = new JsonObject();

        if (options?.MaxOutputTokens is { } limit) configuration["maxOutputTokens"] = limit;
        if (options?.Temperature is { } temperature) configuration["temperature"] = temperature;

        if (configuration.Count > 0) request["generationConfig"] = configuration;

        return request;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => _http.Dispose();
}
