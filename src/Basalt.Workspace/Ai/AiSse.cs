using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Basalt.Workspace.Ai;

/// <summary>Reading a server-sent event stream, which OpenAI and Gemini both speak.</summary>
internal static class AiSse
{
    /// <summary>
    /// The JSON payload of each <c>data:</c> line, until the stream ends or says it is done.
    ///
    /// A payload that is not JSON is skipped: keep-alive and comment lines
    /// carry nothing a turn needs.
    /// </summary>
    public static async IAsyncEnumerable<JsonObject> ReadAsync(
        TextReader reader, [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var payload = line["data:".Length..].Trim();

            if (payload == "[DONE]") yield break;

            JsonObject? node;

            try
            {
                node = JsonNode.Parse(payload) as JsonObject;
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }

            if (node is not null) yield return node;
        }
    }

    /// <summary>Posts a JSON request and gives back the body as a reader, or throws what went wrong.</summary>
    public static async Task<StreamReader> PostAsync(
        HttpClient http, string endpoint, JsonObject body, CancellationToken ct)
    {
        using var content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        var response = await http
            .SendAsync(new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content },
                HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                // The body says what is wrong — an expired key, a model that
                // does not exist — and the user can act on that.
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                throw new InvalidOperationException(AiErrors.Describe(response.StatusCode, error));
            }
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        return new StreamReader(stream);
    }

    /// <summary>The arguments a model wrote, read strictly; an error when they are not one JSON object.</summary>
    public static (JsonObject Input, string? Error) ParseArguments(string json)
    {
        if (json.Trim().Length == 0) return ([], null);

        try
        {
            return JsonNode.Parse(json) is JsonObject obj
                ? (obj, null)
                : ([], "the arguments are not a JSON object");
        }
        catch (System.Text.Json.JsonException ex)
        {
            return ([], ex.Message);
        }
    }
}
