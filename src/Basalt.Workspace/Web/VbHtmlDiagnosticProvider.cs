using Basalt.Extensibility;
using Basalt.Razor.Vb;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Workspace.Web;

/// <summary>
/// The problems the .vbhtml parser found, shown while editing.
///
/// Without this they surface only at build time, as errors from generated
/// code the user never wrote and cannot navigate to. Reporting them against
/// the view itself is the difference between a usable format and one that
/// fails mysteriously.
/// </summary>
public sealed class VbHtmlDiagnosticProvider : IDiagnosticProvider
{
    /// <summary>
    /// Which runtime the view is generated for while answering questions.
    /// </summary>
    /// <remarks>
    /// The generated code must compile against what the user's project
    /// actually references. Generating the standalone shape for an ASP.NET
    /// Core project makes the view inherit a base class that project has
    /// never heard of, and Roslyn then resolves nothing at all — every
    /// question comes back empty, which reads as "nothing to suggest here"
    /// rather than as a fault.
    ///
    /// Standalone by default, because it is the shape that compiles against
    /// the least: a project without ASP.NET Core cannot resolve RazorPage,
    /// and defaulting the other way broke every caller that had none. Whoever
    /// knows the project is a web one says so.
    /// </remarks>
    public ViewHost Host { get; init; } = ViewHost.Standalone;

    /// <summary>
    /// A way to learn a .vbrazor's component catalog before writing it, so
    /// the code compiled for diagnostics is the code the build would compile.
    /// </summary>
    public Func<string, string, CancellationToken, Task<IComponentCatalog?>>? AskCatalog
    { get; init; }

    private readonly HtmlDiagnosticProvider _markup = new();

    private readonly Func<string, CancellationToken,
        Task<IReadOnlyList<Core.Model.IdeDiagnostic>>>? _ask;

    /// <summary>Without a compiler to ask, only the structural checks run.</summary>
    public VbHtmlDiagnosticProvider() { }

    /// <summary>
    /// With a way to compile the generated Visual Basic.
    ///
    /// A typo in a model property is invisible until the build otherwise, and
    /// then it surfaces against code the user never wrote — the failure this
    /// whole bridge exists to prevent.
    /// </summary>
    public VbHtmlDiagnosticProvider(
        Func<string, CancellationToken, Task<IReadOnlyList<Core.Model.IdeDiagnostic>>> ask) =>
        _ask = ask;

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        LanguageDocument document, CancellationToken ct = default)
    {
        var diagnostics = new List<Diagnostic>();

        try
        {
            var parsed = TemplateGeneration.ParseDiagnostics(document.FilePath, document.Text);

            diagnostics.AddRange(parsed.Where(d => d.AppliesTo(document.FilePath)).Select(d => new Diagnostic(
                d.Id,
                d.Message,
                DiagnosticSeverity.Error,
                SourceRange.At(new SourcePosition(d.Line, d.Column)),
                document.FilePath)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A parser that fails outright still leaves the file editable; the
            // failure is reported rather than thrown at the editor.
            diagnostics.Add(new Diagnostic(
                "VBHTML000",
                $"The view could not be parsed: {ex.Message}",
                DiagnosticSeverity.Error,
                SourceRange.At(new SourcePosition(1, 1)),
                document.FilePath));
        }

        // The markup checks apply too: an unclosed tag is worth reporting
        // whether or not the code around it parses.
        diagnostics.AddRange(await _markup.GetDiagnosticsAsync(MarkupOnly(document), ct).ConfigureAwait(false));

        // What the compiler says about the code half, mapped back to the
        // template. Only where the structure held: compiling a template the
        // parser could not read would report errors about the wreckage
        // rather than about what the author wrote.
        if (_ask is not null && !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.AddRange(
                await SemanticDiagnosticsAsync(document, ct).ConfigureAwait(false));
        }

        return diagnostics;
    }

    /// <summary>
    /// The template with its Visual Basic blanked out, for the markup check.
    /// </summary>
    /// <remarks>
    /// Spaces keep every offset, line and column where they were, so what the
    /// markup check reports still lands on the right character; only the code
    /// it must not read as tags is gone. Without this, an attribute such as
    /// &lt;Parameter&gt; in a component's code block was reported as an
    /// element never closed.
    /// </remarks>
    private static LanguageDocument MarkupOnly(LanguageDocument document)
    {
        // A .vbpage is read by its own parser; the spans below are the .vbhtml one's.
        if (TemplateGeneration.IsPage(document.FilePath)) return document;

        IReadOnlyList<(int Start, int Length)> spans;
        try
        {
            spans = VbHtmlCodeRegions.CodeSpans(document.Text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The parser's own failure is reported above; the markup check
            // then reads the text as it stands.
            return document;
        }

        if (spans.Count == 0) return document;

        var characters = document.Text.ToCharArray();
        foreach (var (start, length) in spans)
        {
            for (var i = Math.Max(0, start); i < Math.Min(characters.Length, start + length); i++)
                if (characters[i] is not ('\n' or '\r')) characters[i] = ' ';
        }

        return document with { Text = new string(characters) };
    }

    /// <summary>
    /// Errors the Visual Basic compiler finds, placed on the template.
    ///
    /// A diagnostic that cannot be mapped is dropped rather than shown
    /// somewhere arbitrary: an error on a line the author never wrote is
    /// worse than one they never see.
    /// </summary>
    private async Task<IReadOnlyList<Diagnostic>> SemanticDiagnosticsAsync(
        LanguageDocument document, CancellationToken ct)
    {
        var generated = await TemplateGeneration.ForAsync(
            document.FilePath, Host, document.Text, AskCatalog, ct)
            .ConfigureAwait(false);

        var found = await _ask!(generated.Code, ct).ConfigureAwait(false);

        var mapped = new List<Diagnostic>();

        // Offsets, not line numbers: a mapped region covers several generated
        // lines, and the spans already know how to say which one a position
        // falls in. Matching on the region's first line alone would throw away
        // every error below it.
        var generatedText = SourceText.From(generated.Code);
        var templateText = SourceText.From(document.Text);

        foreach (var problem in found)
        {
            if (problem.Severity != Core.Model.DiagnosticSeverity.Error) continue;

            var offset = OffsetOf(generatedText, problem.Line, problem.Column);

            if (offset is not { } generatedOffset) continue;

            // The line mapping first, as go-to-definition does: on a multi-line
            // code block the span arithmetic drifts by the writer's indentation,
            // and an error on a component's @Code line 8 was reported on line 9.
            // Strict: an error neither can place is an error about the
            // scaffolding, not about anything the author typed.
            var original = TemplateGeneration.StatementLineOriginal(
                                document.FilePath, document.Text, generated, generatedOffset)
                           ?? generated.Map.ToOriginal(generatedOffset);

            if (original is not { } templateOffset) continue;
            if (templateOffset < 0 || templateOffset > templateText.Length) continue;

            var position = templateText.Lines.GetLinePosition(templateOffset);

            mapped.Add(new Diagnostic(
                problem.Id,
                problem.Message,
                DiagnosticSeverity.Error,
                SourceRange.At(new SourcePosition(
                    position.Line + 1, position.Character + 1)),
                document.FilePath));
        }

        return mapped;
    }

    /// <summary>
    /// A 1-based line and column as an offset, or null if the position lies
    /// outside the text.
    /// </summary>
    private static int? OffsetOf(SourceText text, int line, int column)
    {
        var index = line - 1;

        if (index < 0 || index >= text.Lines.Count) return null;

        var textLine = text.Lines[index];
        var offset = textLine.Start + Math.Max(0, column - 1);

        return offset > textLine.End ? textLine.End : offset;
    }
}
