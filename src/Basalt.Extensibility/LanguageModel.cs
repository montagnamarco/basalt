namespace Basalt.Extensibility;

/// <summary>Where something sits in a document. Lines and columns start at 1.</summary>
public readonly record struct SourcePosition(int Line, int Column)
{
    public static SourcePosition Start => new(1, 1);

    public override string ToString() => $"{Line},{Column}";
}

/// <summary>A stretch of a document.</summary>
public readonly record struct SourceRange(SourcePosition Start, SourcePosition End)
{
    public static SourceRange At(SourcePosition position) => new(position, position);
}

public enum DiagnosticSeverity
{
    Hidden,
    Info,
    Warning,
    Error
}

/// <summary>A problem reported against a document.</summary>
public sealed record Diagnostic(
    string Id,
    string Message,
    DiagnosticSeverity Severity,
    SourceRange Range,
    string? FilePath = null)
{
    public override string ToString() =>
        FilePath is null
            ? $"{Severity} {Id}: {Message}"
            : $"{FilePath}({Range.Start}): {Severity} {Id}: {Message}";
}

/// <summary>What a completion entry stands for, used to pick its icon.</summary>
public enum SymbolKind
{
    Unknown,
    Namespace,
    Class,
    Structure,
    Interface,
    Enum,
    Module,
    Delegate,
    Method,
    Function,
    Property,
    Field,
    Constant,
    Event,
    Variable,
    Parameter,
    Keyword,
    Snippet,
    File,
    Label
}

/// <summary>An entry offered while typing.</summary>
public sealed record CompletionItem(
    string DisplayText,
    string InsertionText,
    SymbolKind Kind)
{
    /// <summary>Documentation shown beside the selected entry.</summary>
    public string? Description { get; init; }

    /// <summary>Signature or type, shown after the name.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Text the list filters on, when it differs from what is displayed.
    /// </summary>
    public string? FilterText { get; init; }

    /// <summary>
    /// Higher sorts earlier. Lets a provider put likely entries on top without
    /// the list having to know why they are likely.
    /// </summary>
    public int Priority { get; init; }
}

/// <summary>A symbol declared in a document, as shown in the outline.</summary>
public sealed record DocumentSymbol(
    string Name,
    SymbolKind Kind,
    SourceRange Range)
{
    /// <summary>Signature or type, shown after the name.</summary>
    public string? Detail { get; init; }

    /// <summary>Members declared inside this one.</summary>
    public IReadOnlyList<DocumentSymbol> Children { get; init; } = [];
}

/// <summary>Where a symbol is declared or used.</summary>
public sealed record SourceLocation(string FilePath, SourceRange Range);

/// <summary>Information shown when hovering over a symbol.</summary>
public sealed record QuickInfo(string Signature, string? Documentation = null);

/// <summary>One overload shown while typing an argument list.</summary>
public sealed record SignatureInfo(
    string Signature,
    IReadOnlyList<string> Parameters)
{
    public string? Documentation { get; init; }
}

/// <summary>Overloads available at the caret, and which argument is being typed.</summary>
public sealed record SignatureHelp(
    IReadOnlyList<SignatureInfo> Signatures,
    int ActiveSignature,
    int ActiveParameter);

/// <summary>A replacement to apply to a document.</summary>
public readonly record struct TextEdit(int Start, int Length, string NewText);

/// <summary>Result of formatting, with where the caret ends up.</summary>
public sealed record FormattingResult(string Text, int Caret, bool Changed)
{
    public static FormattingResult Unchanged(string text, int caret) => new(text, caret, false);
}
