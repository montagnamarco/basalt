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

    /// <summary>
    /// What committing the entry really writes, as the language service works
    /// it out — Text.StringBuilder where the list filtered on StringBuilder, a
    /// generic's type argument list, a qualifier the file does not import.
    /// Asked only for the entry committed, since it costs a compilation.
    /// </summary>
    public Func<CancellationToken, Task<CompletionCommit?>>? ResolveCommit { get; init; }
}

/// <summary>
/// The edit a completion makes, in the text the list was asked about: replace
/// Length characters at Start with Text, and leave the caret at Caret.
/// </summary>
public sealed record CompletionCommit(int Start, int Length, string Text, int? Caret);
