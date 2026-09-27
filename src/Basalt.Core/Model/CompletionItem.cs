namespace Basalt.Core.Model;

public enum CompletionKind
{
    Other,
    Class,
    Structure,
    Interface,
    Enum,
    Method,
    Property,
    Field,
    Event,
    Local,
    Parameter,
    Namespace,
    Keyword,
    Snippet
}

/// <summary>Completion entry presented by the editor.</summary>
public sealed record CompletionItem(
    string DisplayText,
    string InsertionText,
    CompletionKind Kind,
    string? Description = null)
{
    /// <summary>
    /// The entry to select when the list opens before anything is typed: the
    /// declared type after New, the enum's members after "=". The language
    /// service says which, as it does for Visual Studio.
    /// </summary>
    public bool IsPreselected { get; init; }
}
