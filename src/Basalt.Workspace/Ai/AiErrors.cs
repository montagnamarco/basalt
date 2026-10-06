using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basalt.Core.Localization;

namespace Basalt.Workspace.Ai;

/// <summary>Turns a failed request into something worth reading.</summary>
public static class AiErrors
{
    /// <summary>
    /// Whether a vendor's error says the conversation no longer fits its
    /// window: Claude "prompt is too long", OpenAI "context_length_exceeded",
    /// Gemini "input token count ... exceeds the maximum".
    /// </summary>
    public static bool IsContextTooLong(string message) =>
        new[] { "prompt is too long", "context_length_exceeded", "maximum context length",
                "exceeds the maximum number of tokens", "input token count", "context window" }
            .Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static string Describe(HttpStatusCode status, string body)
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
            HttpStatusCode.Unauthorized =>
                Localizer.Get(StringKeys.AiErrorKeyRefused),
            HttpStatusCode.TooManyRequests =>
                Localizer.Get(StringKeys.AiErrorRateLimited),
            _ => Localizer.Get(StringKeys.AiErrorStatus, (int)status, message)
        };
    }
}
