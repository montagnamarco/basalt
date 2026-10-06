using System.Text;
using System.Text.Json.Nodes;
using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>Who said something in a conversation.</summary>
public enum AiRole { User, Assistant }

/// <summary>How hard the assistant is asked to think.</summary>
public enum AiEffort { Low, Medium, High }

/// <summary>Why an assistant stopped producing a turn.</summary>
public enum AiStopReason
{
    /// <summary>It finished what it was saying.</summary>
    EndTurn,

    /// <summary>It wants tools run and the results sent back.</summary>
    ToolUse,

    /// <summary>It ran out of room mid-answer; anything cut off is unreliable.</summary>
    MaxTokens,

    /// <summary>A safety classifier or the model itself declined.</summary>
    Refusal,

    Other
}

/// <summary>A piece of a message: text, a tool call, or the result of one.</summary>
public abstract record AiPart;

/// <summary>
/// Text. <paramref name="CacheBreakpoint"/> asks a vendor that caches prompts to
/// cache everything up to and including this block.
/// </summary>
public sealed record AiTextPart(string Text, bool CacheBreakpoint = false) : AiPart;

/// <summary>
/// The assistant asking for a tool to be run.
///
/// <see cref="InputError"/> is set when the arguments could not be read as
/// JSON: the call is still answered (every call needs an answer), with the
/// error, so the assistant can try again.
/// </summary>
public sealed record AiToolCallPart(string Id, string Name, JsonObject Input, string? InputError = null)
    : AiPart;

public sealed record AiToolResultPart(string CallId, string Name, string Content, bool IsError) : AiPart;

/// <summary>
/// One message of a conversation, in no vendor's shape.
///
/// <see cref="Native"/> is whatever the backend that produced the message
/// needs to say it back exactly as it was said (Claude's signed thinking
/// blocks, Gemini's thought signatures); no other code looks inside it.
/// </summary>
public sealed class AiMessage
{
    public AiMessage(AiRole role, IEnumerable<AiPart> parts, object? native = null)
    {
        Role = role;
        Parts = [.. parts];
        Native = native;
    }

    public AiRole Role { get; }

    public IReadOnlyList<AiPart> Parts { get; }

    public object? Native { get; }

    /// <summary>The text of the message, without tool traffic.</summary>
    public string Text => string.Concat(Parts.OfType<AiTextPart>().Select(p => p.Text));

    public IReadOnlyList<AiToolCallPart> ToolCalls => [.. Parts.OfType<AiToolCallPart>()];

    public static AiMessage User(string text) => new(AiRole.User, [new AiTextPart(text)]);
}

/// <summary>What a tool handler gives back.</summary>
/// <param name="Content">The text the assistant reads.</param>
/// <param name="IsError">The tool could not do what was asked; the assistant is told so.</param>
/// <param name="Proposal">A change the assistant proposes, for the user to review.</param>
public sealed record AiToolResult(string Content, bool IsError = false, AiChangeSet? Proposal = null)
{
    public static AiToolResult Error(string message) => new(message, IsError: true);
}

/// <summary>Runs a tool; throws <see cref="AiToolException"/> for a failure worth telling the assistant.</summary>
public delegate Task<AiToolResult> AiToolHandler(JsonObject input, CancellationToken ct);

/// <summary>A failure of a tool the assistant should read and react to.</summary>
public sealed class AiToolException(string message) : Exception(message);

/// <summary>
/// Something the assistant may ask the IDE to do.
///
/// A name, a description, a JSON schema for the arguments and a handler: the
/// same four things every vendor asks for, so each backend only translates the
/// declaration and the loop runs the handler.
/// </summary>
public sealed class AiTool(string name, string description, JsonObject inputSchema, AiToolHandler handler)
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    /// <summary>An object schema: <c>type</c>, <c>properties</c> and <c>required</c>.</summary>
    public JsonObject InputSchema { get; } = inputSchema;

    public AiToolHandler Handler { get; } = handler;

    /// <summary>
    /// Whether the arguments fit the schema, and what is wrong when they do not.
    ///
    /// The model's output is untrusted: with eager input streaming the API no
    /// longer validates it, and a truncated or malformed object can parse.
    /// </summary>
    public string? Validate(JsonObject input) => AiSchema.Validate(input, InputSchema, "arguments");
}

/// <summary>
/// A small JSON-schema checker: types, required properties, enums and array
/// items, which is all the tool schemas use.
/// </summary>
internal static class AiSchema
{
    public static string? Validate(JsonNode? value, JsonObject schema, string path)
    {
        var type = schema["type"]?.GetValue<string>();

        if (value is null)
            return $"{path} is missing.";

        switch (type)
        {
            case "object":
                if (value is not JsonObject obj) return $"{path} must be an object.";

                if (schema["required"] is JsonArray required)
                {
                    foreach (var name in required.Select(r => r!.GetValue<string>()))
                        if (obj[name] is null) return $"{path}.{name} is required.";
                }

                if (schema["properties"] is JsonObject properties)
                {
                    foreach (var (name, propertySchema) in properties)
                    {
                        if (obj[name] is not { } member || propertySchema is not JsonObject memberSchema)
                            continue;

                        if (Validate(member, memberSchema, $"{path}.{name}") is { } problem)
                            return problem;
                    }
                }

                return null;

            case "array":
                if (value is not JsonArray array) return $"{path} must be an array.";

                if (schema["items"] is JsonObject items)
                {
                    for (var i = 0; i < array.Count; i++)
                        if (Validate(array[i], items, $"{path}[{i}]") is { } problem)
                            return problem;
                }

                return null;

            case "string":
                if (value is not JsonValue text || !text.TryGetValue<string>(out var s))
                    return $"{path} must be a string.";

                if (schema["enum"] is JsonArray allowed
                    && !allowed.Any(a => a?.GetValue<string>() == s))
                    return $"{path} must be one of: {string.Join(", ", allowed.Select(a => a!.GetValue<string>()))}.";

                return null;

            case "integer":
                return TryNumber(value) is { } whole && whole == Math.Floor(whole)
                    ? null
                    : $"{path} must be an integer.";

            case "number":
                return TryNumber(value) is not null ? null : $"{path} must be a number.";

            case "boolean":
                return value is JsonValue b && b.TryGetValue<bool>(out _) ? null : $"{path} must be true or false.";

            default:
                return null;
        }
    }

    /// <summary>
    /// A JSON number as a double, however the node was made: parsed from text
    /// or built from an int, which a plain TryGetValue treats as different types.
    /// </summary>
    internal static double? TryNumber(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number
        && double.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    /// <summary>Builds an object schema from property declarations, for tool authors.</summary>
    public static JsonObject Object(IEnumerable<(string Name, JsonObject Schema)> properties,
        params string[] required)
    {
        var props = new JsonObject();
        foreach (var (name, schema) in properties) props[name] = schema;

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray([.. required.Select(r => (JsonNode)JsonValue.Create(r)!)])
        };
    }

    public static JsonObject Property(string type, string description) =>
        new() { ["type"] = type, ["description"] = description };
}

/// <summary>
/// What one request to the assistant carries.
///
/// Nothing in it ever changes after it has been sent: the system text and the
/// tools are the same on every request, and each earlier message is exactly as
/// it was. That is what keeps the prompt cache valid and, on the models that
/// sign their thinking to the conversation, the thinking usable. What varies
/// (the project, the active file) travels inside the newest user message.
/// </summary>
/// <param name="StableSystem">The instructions that never change.</param>
/// <param name="Brief">
/// A short completion rather than a conversation turn: the output is capped
/// at <paramref name="MaxTokens"/> (a backend that otherwise allows a long
/// answer must not), and a model is not rerouted to another one.
/// </param>
public sealed record AiTurnRequest(
    string StableSystem,
    IReadOnlyList<AiTool> Tools,
    IReadOnlyList<AiMessage> Messages,
    AiEffort Effort,
    int MaxTokens,
    bool Brief = false)
{
    /// <summary>The system text for assistants that take one string.</summary>
    public string SystemText() => StableSystem;
}

/// <summary>Something that happens while an assistant produces one turn.</summary>
public abstract record AiTurnEvent;

public sealed record AiTextDelta(string Text) : AiTurnEvent;

/// <summary>
/// The turn is over.
/// </summary>
/// <param name="Message">What the assistant said, tool calls included.</param>
/// <param name="Stop">Why it stopped.</param>
/// <param name="StopCategory">For a refusal, the policy category when the API gave one.</param>
/// <param name="ModelUsed">The model that actually answered.</param>
/// <param name="FellBack">A different model than the one asked for answered.</param>
public sealed record AiTurnEnd(
    AiMessage Message,
    AiStopReason Stop,
    string? StopCategory = null,
    string? ModelUsed = null,
    bool FellBack = false) : AiTurnEvent;

/// <summary>An assistant that can take a turn in a tool-using conversation.</summary>
public interface IAiChatBackend : IDisposable
{
    /// <summary>
    /// Streams one assistant turn: text as it arrives, then one <see cref="AiTurnEnd"/>.
    /// </summary>
    IAsyncEnumerable<AiTurnEvent> StreamTurnAsync(AiTurnRequest request, CancellationToken ct);
}

/// <summary>An assistant vendor the IDE can hold a tool-using conversation with.</summary>
public interface IAiChatProvider : IAiProvider
{
    /// <summary>
    /// A backend for one conversation.
    ///
    /// The key is passed rather than stored here: a provider that held one
    /// would keep it alive for as long as the IDE runs. Any model id works,
    /// not only the ones listed.
    /// </summary>
    IAiChatBackend CreateBackend(string apiKey, string modelId);
}

/// <summary>Text helpers shared by the tools and the loop.</summary>
public static class AiText
{
    /// <summary>
    /// At most <paramref name="max"/> characters, never ending in half of a
    /// surrogate pair (an emoji cut in two is invalid text to a vendor).
    /// </summary>
    public static string Truncate(string text, int max, string suffix = "")
    {
        if (text.Length <= max) return text;

        var end = max;

        if (end > 0 && char.IsHighSurrogate(text[end - 1])) end--;

        return text[..end] + suffix;
    }
}
