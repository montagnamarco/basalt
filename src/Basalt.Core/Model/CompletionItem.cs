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
    /// How an entry ranks against equally good matches, higher first. The
    /// language service sets it where it knows what is common at the position:
    /// after "As" the Integer and the String come before the rest of the types,
    /// which the list would otherwise sort alphabetically.
    /// </summary>
    public int Rank { get; init; }

    /// <summary>
    /// The text the list matches what was typed against, when it differs from
    /// what is inserted: a Razor directive attribute is inserted as "@onclick"
    /// and filtered as "onclick". Null matches the insertion text.
    /// </summary>
    public string? FilterText { get; init; }

    /// <summary>
    /// The namespace or type a name belongs to. Two entries with one name from
    /// different containers, "Timer" from System.Threading and from System.Timers,
    /// are two rows; a name with no container, a keyword or a local, is one row
    /// with any of them.
    /// </summary>
    public string? Container { get; init; }

    /// <summary>
    /// Set on every entry of a list in suggestion mode: the language service is
    /// asking for a new name, as in "For Each item In items", where the name
    /// being declared is not yet a name. A space or punctuation then never
    /// commits an entry that is not the name written in full.
    /// </summary>
    public bool SoftSelection { get; init; }

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
