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
/// This describes the assistant: its name, its models and where to get a key.
/// Talking to it is the workspace layer's business (see IAiChatProvider), so
/// the panel works the same whichever assistant is chosen.
/// </summary>
public interface IAiProvider
{
    AiVendor Vendor { get; }

    string DisplayName { get; }

    /// <summary>The models this assistant offers, newest first.</summary>
    IReadOnlyList<AiModel> Models { get; }

    /// <summary>Where the user gets a key, shown when none is set.</summary>
    string ApiKeyUrl { get; }
}
