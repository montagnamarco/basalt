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
    string? Description = null);
