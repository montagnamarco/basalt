using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Web;
using IdeDiagnostic = Basalt.Core.Model.IdeDiagnostic;
using Basalt.Extensibility;
using Basalt.Workspace.Completion;

namespace Basalt.Workspace.Web;

/// <summary>
/// Completion for HTML, offered according to what the caret is in.
///
/// Reached through <see cref="ILanguageProvider"/> like every other language,
/// so nothing in the shell needs to know that HTML is handled differently from
/// Visual Basic.
/// </summary>
public sealed class HtmlCompletionProvider : ICompletionProvider
{
    public Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var context = HtmlContextReader.At(document.Text, position);

        IReadOnlyList<CompletionItem> items = context.Kind switch
        {
            HtmlContextKind.ElementName => Elements(),
            HtmlContextKind.AttributeName => Attributes(context.Element),
            HtmlContextKind.AttributeValue => Values(context.Element, context.Attribute),

            // In content only a new element makes sense, and only once "<" is
            // typed; offering the whole vocabulary into running text would be
            // noise.
            _ => []
        };

        if (items.Count == 0 || context.Prefix.Length == 0)
            return Task.FromResult(items);

        return Task.FromResult<IReadOnlyList<CompletionItem>>(
            [.. Rank(items, context.Prefix)]);
    }

    private static IReadOnlyList<CompletionItem> Elements() =>
        [.. HtmlLanguage.Elements.Select(name => new CompletionItem(
            name, name, SymbolKind.Class)
        {
            Description = HtmlLanguage.VoidElements.Contains(name)
                ? "Element without a closing tag."
                : "Element."
        })];

    private static IReadOnlyList<CompletionItem> Attributes(string element) =>
        [.. HtmlLanguage.AttributesFor(element).Select(name => new CompletionItem(
            name, name, SymbolKind.Property) { Description = "Attribute." })];

    private static IReadOnlyList<CompletionItem> Values(string element, string attribute) =>
        [.. HtmlLanguage.ValuesFor(element, attribute).Select(value => new CompletionItem(
            value, value, SymbolKind.Constant) { Description = "Value." })];

    /// <summary>
    /// Narrows and orders by what has been typed.
    ///
    /// The same matcher the code languages use, so "di" behaves the same way
    /// in markup as it does in Visual Basic.
    /// </summary>
    private static IEnumerable<CompletionItem> Rank(
        IReadOnlyList<CompletionItem> items, string prefix)
    {
        var core = items
            .Select(i => new Basalt.Core.Model.CompletionItem(
                i.DisplayText, i.InsertionText, Basalt.Core.Model.CompletionKind.Other))
            .ToList();

        var ranked = CompletionMatcher.Filter(core, prefix);

        return ranked
            .Select(match => items.First(i => i.DisplayText == match.Item.DisplayText));
    }
}

/// <summary>Completion for stylesheets.</summary>
public sealed class CssCompletionProvider : ICompletionProvider
{
    public Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var context = CssContextReader.At(document.Text, position);

        IReadOnlyList<CompletionItem> items = context.Kind switch
        {
            CssContextKind.PropertyName =>
                [.. CssLanguage.Properties.Select(p => new CompletionItem(
                    p, p + ": ", SymbolKind.Property) { Description = "Property." })],

            CssContextKind.PropertyValue =>
                [.. CssLanguage.ValuesFor(context.Property).Select(v => new CompletionItem(
                    v, v, SymbolKind.Constant) { Description = "Value." })],

            // At the top level only the at-rules are a closed set; a selector
            // is whatever the document's own markup calls for.
            CssContextKind.Selector =>
                [.. CssLanguage.AtRules.Select(r => new CompletionItem(
                    r, r, SymbolKind.Keyword) { Description = "At-rule." })],

            _ => []
        };

        if (items.Count == 0 || context.Prefix.Length == 0)
            return Task.FromResult(items);

        var matching = items
            .Where(i => i.DisplayText.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)
                     || i.DisplayText.Contains(context.Prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.DisplayText.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(i => i.DisplayText.Length)
            .ThenBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<CompletionItem>>(matching);
    }
}

/// <summary>
/// Structural problems in markup, as far as they can be judged locally.
///
/// Deliberately limited to what is certainly wrong: a tag closed with the
/// wrong name, or left unclosed. Anything more would report errors in Razor
/// views, where the markup is generated in part by the code around it.
/// </summary>
public sealed class HtmlDiagnosticProvider : IDiagnosticProvider
{
    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        LanguageDocument document, CancellationToken ct = default)
    {
        var diagnostics = new List<Diagnostic>();
        var open = new Stack<(string Name, int Line, int Column)>();

        foreach (var tag in HtmlTagScanner.Scan(document.Text))
        {
            ct.ThrowIfCancellationRequested();

            if (HtmlLanguage.VoidElements.Contains(tag.Name) || tag.SelfClosing) continue;

            if (!tag.Closing)
            {
                open.Push((tag.Name, tag.Line, tag.Column));
                continue;
            }

            if (open.Count == 0)
            {
                diagnostics.Add(Error(
                    $"Closing tag '</{tag.Name}>' has no matching opening tag.",
                    document.FilePath, tag.Line, tag.Column));

                continue;
            }

            var expected = open.Pop();

            if (!expected.Name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    $"Expected '</{expected.Name}>' but found '</{tag.Name}>'.",
                    document.FilePath, tag.Line, tag.Column));
            }
        }

        foreach (var (name, line, column) in open)
            diagnostics.Add(Error($"'<{name}>' is never closed.", document.FilePath, line, column));

        return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
    }

    private static Diagnostic Error(string message, string filePath, int line, int column) =>
        new("HTML001", message, DiagnosticSeverity.Warning,
            SourceRange.At(new SourcePosition(line, column)), filePath);
}

/// <summary>A tag found in the markup.</summary>
public readonly record struct HtmlTag(
    string Name, bool Closing, bool SelfClosing, int Line, int Column);

/// <summary>
/// Finds the tags in a document.
///
/// Scanned rather than parsed: a Razor view is not well-formed markup until
/// its code has run, and a parser would report that as a fault.
/// </summary>
public static class HtmlTagScanner
{
    public static IEnumerable<HtmlTag> Scan(string text)
    {
        var line = 1;
        var column = 1;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            if (text[i] != '<') { column++; continue; }

            // A comment is skipped whole: the markup inside one is text, and
            // stepping over only the "<!" would find the tags written in it.
            if (text.AsSpan(i).StartsWith("<!--"))
            {
                var commentEnd = text.IndexOf("-->", i, StringComparison.Ordinal);
                if (commentEnd < 0) yield break;

                for (var j = i; j < commentEnd + 3; j++)
                {
                    if (text[j] == '\n') { line++; column = 1; }
                    else column++;
                }

                i = commentEnd + 2;
                continue;
            }

            // Doctypes and processing instructions are not tags either.
            if (i + 1 < text.Length && text[i + 1] is '!' or '?')
            {
                column++;
                continue;
            }

            var end = text.IndexOf('>', i);
            if (end < 0) yield break;

            var inner = text[(i + 1)..end];

            if (inner.Length > 0)
            {
                var closing = inner[0] == '/';
                var selfClosing = inner[^1] == '/';

                var name = inner.TrimStart('/').TrimEnd('/');
                var space = name.IndexOfAny([' ', '\t', '\n', '\r']);
                if (space >= 0) name = name[..space];

                if (name.Length > 0 && (char.IsLetter(name[0])))
                    yield return new HtmlTag(name, closing, selfClosing, line, column);
            }

            // Line and column must follow the text skipped over.
            for (var j = i; j <= end && j < text.Length; j++)
            {
                if (text[j] == '\n') { line++; column = 1; }
                else column++;
            }

            i = end;
        }
    }
}

/// <summary>
/// The web languages, offered through the same interfaces as every other.
///
/// Registering these is all it takes for the IDE to complete and check HTML
/// and CSS: nothing in the shell is aware they are handled by hand-written
/// tables rather than by a compiler.
/// </summary>
public static class WebLanguageProviders
{
    public static ILanguageProvider Html { get; } = new WebProvider(
        new LanguageIdentity("html", "HTML", [".html", ".htm", ".xhtml"], isCaseSensitive: false),
        new HtmlCompletionProvider(),
        new HtmlDiagnosticProvider());

    public static ILanguageProvider Css { get; } = new WebProvider(
        new LanguageIdentity("css", "CSS", [".css", ".scss", ".less"], isCaseSensitive: false),
        new CssCompletionProvider(),
        diagnostics: null);

    /// <summary>
    /// Razor for Visual Basic.
    ///
    /// The markup half is ordinary HTML, so it gets the same completion and
    /// the same structural checks. What is inside the code blocks is the
    /// generator's business.
    /// </summary>
    public static ILanguageProvider VbRazor { get; } = new WebProvider(
        // .vbrazor alongside: a Blazor component carries the same syntax as a
        // view and asks the same questions of the editor. Listing only the
        // view left a component with no provider at all, so it coloured — that
        // comes from the parser — and answered nothing else.
        new LanguageIdentity("vbhtml", "Razor (Visual Basic)", [".vbhtml", ".vbrazor", ".vbpage"], isCaseSensitive: false),
        new VbHtmlCompletionProvider(),
        new VbHtmlDiagnosticProvider(),
        new VbHtmlFormattingProvider(),
        new VbHtmlNavigationProvider());

    /// <summary>
    /// The Razor provider, able to ask a language service about the code half.
    ///
    /// Without one the markup half still answers and the code half offers
    /// nothing, which is honest; with one the code half offers what the
    /// compiler actually knows.
    /// </summary>
    public static ILanguageProvider VbRazorAsking(
        Func<string, int, CancellationToken, Task<IReadOnlyList<CompletionItem>>> ask,
        Func<string, int, CancellationToken, Task<QuickInfo?>>? askQuickInfo = null,
        Func<string, CancellationToken, Task<IReadOnlyList<IdeDiagnostic>>>?
            askDiagnostics = null,
        Func<string, int, CancellationToken, Task<SourceLocation?>>?
            askDefinition = null,
        Func<string, int, CancellationToken, Task<IReadOnlyList<SourceLocation>>>?
            askReferences = null,
        Func<string, int, CancellationToken, Task<SignatureHelp?>>?
            askSignature = null,
        ViewHost host = ViewHost.Standalone,
        // Learns a .vbrazor's component catalog before it is written, the
        // way the build learns it, so a named RenderFragment parameter, a
        // typed RenderFragment's @context and a generic component's
        // inferred type arguments read the same in the editor as they
        // compile in the build. Null leaves every writer exactly as it
        // behaved before catalogs existed.
        Func<string, string, CancellationToken, Task<IComponentCatalog?>>? askCatalog = null) =>
        new WebProvider(
            new LanguageIdentity("vbhtml", "Razor (Visual Basic)", [".vbhtml", ".vbrazor", ".vbpage"],
                isCaseSensitive: false),
            askSignature is null
                ? new VbHtmlCompletionProvider(ask) { Host = host, AskCatalog = askCatalog }
                : new VbHtmlCompletionProvider(ask, askSignature) { Host = host, AskCatalog = askCatalog },
            askDiagnostics is null
                ? new VbHtmlDiagnosticProvider { Host = host, AskCatalog = askCatalog }
                : new VbHtmlDiagnosticProvider(askDiagnostics) { Host = host, AskCatalog = askCatalog },
            new VbHtmlFormattingProvider(),
            askQuickInfo is null && askDefinition is null && askReferences is null
                ? new VbHtmlNavigationProvider { Host = host, AskCatalog = askCatalog }
                : new VbHtmlNavigationProvider(new VbHtmlNavigationProvider.Questions
                {
                    QuickInfo = askQuickInfo,
                    Definition = askDefinition,
                    References = askReferences,
                })
                { Host = host, AskCatalog = askCatalog });

    /// <summary>Registers all of them with a registry.</summary>
    public static void RegisterAll(LanguageRegistry registry) =>
        RegisterAll(registry, ask: null);

    /// <summary>
    /// Registers them, giving Razor a way to ask about Visual Basic where one
    /// is available.
    /// </summary>
    public static void RegisterAll(
        LanguageRegistry registry,
        Func<string, int, CancellationToken, Task<IReadOnlyList<CompletionItem>>>? ask,
        Func<string, int, CancellationToken, Task<QuickInfo?>>? askQuickInfo = null,
        Func<string, CancellationToken, Task<IReadOnlyList<IdeDiagnostic>>>?
            askDiagnostics = null,
        Func<string, int, CancellationToken, Task<SourceLocation?>>?
            askDefinition = null,
        Func<string, int, CancellationToken, Task<IReadOnlyList<SourceLocation>>>?
            askReferences = null,
        Func<string, int, CancellationToken, Task<SignatureHelp?>>?
            askSignature = null,
        Func<string, string, CancellationToken, Task<IComponentCatalog?>>? askCatalog = null)
    {
        registry.Register(Html);
        registry.Register(Css);
        registry.Register(ask is null
            ? VbRazor
            : VbRazorAsking(
                ask, askQuickInfo, askDiagnostics, askDefinition, askReferences,
                askSignature, askCatalog: askCatalog));
    }

    private sealed class WebProvider : ILanguageProvider
    {
        public WebProvider(
            LanguageIdentity identity,
            ICompletionProvider? completion,
            IDiagnosticProvider? diagnostics,
            IFormattingProvider? formatting = null,
            INavigationProvider? navigation = null)
        {
            Identity = identity;
            Completion = completion;
            Diagnostics = diagnostics;
            Formatting = formatting;
            Navigation = navigation;
        }

        public LanguageIdentity Identity { get; }
        public ICompletionProvider? Completion { get; }
        public IDiagnosticProvider? Diagnostics { get; }

        // Colouring comes from the TextMate grammars in the shell, which are
        // the ones Visual Studio Code uses; there is nothing better to add here.
        public ISyntaxHighlightProvider? Highlighting => null;

        public INavigationProvider? Navigation { get; }
        public IFormattingProvider? Formatting { get; }
        public ICompilerBackend? Compiler => null;

        public Task OpenSolutionAsync(string path, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
