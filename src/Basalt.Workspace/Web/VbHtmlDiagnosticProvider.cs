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
            var parsed = VbHtmlParser.Parse(document.Text);

            diagnostics.AddRange(parsed.Diagnostics.Select(d => new Diagnostic(
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
        diagnostics.AddRange(await _markup.GetDiagnosticsAsync(document, ct).ConfigureAwait(false));

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
    /// Errors the Visual Basic compiler finds, placed on the template.
    ///
    /// A diagnostic that cannot be mapped is dropped rather than shown
    /// somewhere arbitrary: an error on a line the author never wrote is
    /// worse than one they never see.
    /// </summary>
    private async Task<IReadOnlyList<Diagnostic>> SemanticDiagnosticsAsync(
        LanguageDocument document, CancellationToken ct)
    {
        var generated = TemplateGeneration.For(VbHtmlParser.Parse(document.Text), document.FilePath, Host);

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

            // Strict: an error the map cannot place is an error about the
            // scaffolding, not about anything the author typed.
            var original = generated.Map.ToOriginal(generatedOffset);

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
