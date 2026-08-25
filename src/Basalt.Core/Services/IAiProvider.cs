using Microsoft.Extensions.AI;

namespace Basalt.Core.Services;

/// <summary>Which assistant to talk to.</summary>
public enum AiVendor { Claude, Gemini, ChatGpt }

/// <summary>A model an assistant offers.</summary>
public sealed record AiModel(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// An assistant the IDE can talk to.
///
/// Built on <see cref="IChatClient"/> so the panel talks to one interface
/// whichever assistant is chosen, and a fourth could be added without the
/// panel changing.
/// </summary>
public interface IAiProvider
{
    AiVendor Vendor { get; }

    string DisplayName { get; }

    /// <summary>The models this assistant offers, newest first.</summary>
    IReadOnlyList<AiModel> Models { get; }

    /// <summary>Where the user gets a key, shown when none is set.</summary>
    string ApiKeyUrl { get; }

    /// <summary>
    /// A client for talking to the assistant.
    ///
    /// The key is passed rather than stored here: a provider that held one
    /// would keep it alive for as long as the IDE runs.
    /// </summary>
    IChatClient CreateClient(string apiKey, string modelId);
}
