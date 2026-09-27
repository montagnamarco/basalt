using Basalt.Extensibility;
using Microsoft.CodeAnalysis;

namespace Basalt.Workspace.Languages;

/// <summary>
/// What Visual Basic can say about a symbol, in the shared form.
///
/// The description is built from Roslyn's own display parts rather than from
/// a formatted string: a tooltip needs to know which run is the parameter
/// being written so it can put that one in bold, and a string cannot say.
/// </summary>
internal sealed class RoslynSymbolDescriptionProvider : ISymbolDescriptionProvider
{
    private readonly RoslynLanguageService _service;

    public RoslynSymbolDescriptionProvider(RoslynLanguageService service) =>
        _service = service;

    public async Task<SymbolDescription?> DescribeAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var symbol = await _service
            .FindSymbolAsync(document.FilePath, position, document.Text, ct)
            .ConfigureAwait(false);

        if (symbol is null) return null;

        return Describe(symbol, ct);
    }

    public Task<SymbolDescriptionSet?> DescribeCallAsync(
        LanguageDocument document, int position, CancellationToken ct = default) =>
        // Cached Roslyn awaits can complete synchronously. Keep the whole
        // request, including symbol display and documentation, off the caller.
        Task.Run<SymbolDescriptionSet?>(async () =>
        {
            var call = await _service
                .FindCallAsync(document.FilePath, position, document.Text, ct)
                .ConfigureAwait(false);

            if (call is not { } found || found.Methods.Count == 0) return null;

            var overloads = found.Methods
                .Select(m => Describe(m, ct) with { ActiveParameter = found.ActiveParameter })
                .ToList();

            return new SymbolDescriptionSet(overloads) { Active = 0 };
        }, ct);

    /// <summary>
    /// A symbol as parts, taking Roslyn's own classification of each run.
    /// </summary>
    internal static SymbolDescription Describe(ISymbol symbol, CancellationToken ct = default)
    {
        var format = SymbolDisplayFormat.MinimallyQualifiedFormat
            .WithMemberOptions(
                SymbolDisplayMemberOptions.IncludeParameters
                | SymbolDisplayMemberOptions.IncludeType
                | SymbolDisplayMemberOptions.IncludeContainingType)
            .WithParameterOptions(
                SymbolDisplayParameterOptions.IncludeName
                | SymbolDisplayParameterOptions.IncludeType
                | SymbolDisplayParameterOptions.IncludeDefaultValue);

        var parts = symbol.ToDisplayParts(format)
            .Select(part => new SymbolPart(part.ToString(), KindOf(part.Kind)))
            .ToList();

        var parameters = symbol is IMethodSymbol method
            ? method.Parameters.Select(p => new ParameterDescription(
                p.Name,
                p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            {
                Documentation = ParameterDocumentation(symbol, p.Name, ct)
            }).ToList()
            : [];

        return new SymbolDescription(parts)
        {
            Parameters = parameters,
            Documentation = Summary(symbol.GetDocumentationCommentXml(cancellationToken: ct)),
            Kind = KindOf(symbol)
        };
    }

    /// <summary>
    /// Roslyn's classification of a run, reduced to what a tooltip draws.
    ///
    /// Roslyn distinguishes far more than a tooltip can usefully show; what
    /// matters is telling a parameter name from a type from punctuation.
    /// </summary>
    private static SymbolPartKind KindOf(SymbolDisplayPartKind kind) => kind switch
    {
        SymbolDisplayPartKind.Keyword => SymbolPartKind.Keyword,
        SymbolDisplayPartKind.ParameterName => SymbolPartKind.ParameterName,

        SymbolDisplayPartKind.ClassName or SymbolDisplayPartKind.StructName
            or SymbolDisplayPartKind.InterfaceName or SymbolDisplayPartKind.EnumName
            or SymbolDisplayPartKind.DelegateName or SymbolDisplayPartKind.TypeParameterName
            => SymbolPartKind.Type,

        SymbolDisplayPartKind.MethodName or SymbolDisplayPartKind.PropertyName
            or SymbolDisplayPartKind.FieldName or SymbolDisplayPartKind.LocalName
            or SymbolDisplayPartKind.EventName => SymbolPartKind.Name,

        _ => SymbolPartKind.Plain
    };

    private static Extensibility.SymbolKind KindOf(ISymbol symbol) => symbol.Kind switch
    {
        Microsoft.CodeAnalysis.SymbolKind.Method => Extensibility.SymbolKind.Method,
        Microsoft.CodeAnalysis.SymbolKind.Property => Extensibility.SymbolKind.Property,
        Microsoft.CodeAnalysis.SymbolKind.Field => Extensibility.SymbolKind.Field,
        Microsoft.CodeAnalysis.SymbolKind.Local => Extensibility.SymbolKind.Variable,
        Microsoft.CodeAnalysis.SymbolKind.Parameter => Extensibility.SymbolKind.Variable,
        Microsoft.CodeAnalysis.SymbolKind.NamedType => Extensibility.SymbolKind.Class,
        Microsoft.CodeAnalysis.SymbolKind.Namespace => Extensibility.SymbolKind.Namespace,
        _ => Extensibility.SymbolKind.Unknown
    };

    /// <summary>The summary from a documentation comment, if there is one.</summary>
    private static string? Summary(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        return Between(xml!, "<summary>", "</summary>");
    }

    /// <summary>What the author wrote about one parameter.</summary>
    private static string? ParameterDocumentation(
        ISymbol symbol, string name, CancellationToken ct)
    {
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(xml)) return null;

        return Between(xml!, $"<param name=\"{name}\">", "</param>");
    }

    private static string? Between(string xml, string open, string close)
    {
        var from = xml.IndexOf(open, StringComparison.Ordinal);

        if (from < 0) return null;

        from += open.Length;

        var to = xml.IndexOf(close, from, StringComparison.Ordinal);

        if (to < 0) return null;

        var text = xml[from..to].Trim();

        // Documentation comments arrive with the line breaks and indentation
        // they were written with; a tooltip wants one paragraph.
        return string.Join(" ", text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()));
    }
}
