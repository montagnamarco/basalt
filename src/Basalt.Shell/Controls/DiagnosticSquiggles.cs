using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Basalt.Core.Model;

namespace Basalt.Shell.Controls;

/// <summary>
/// Draws the wavy underlines beneath errors and warnings.
///
/// Rendered as a background layer rather than as text styling: a squiggle sits
/// under the glyphs without changing their colour, so syntax highlighting stays
/// intact underneath it.
/// </summary>
public sealed class DiagnosticSquiggles : IBackgroundRenderer
{
    private readonly TextDocument _document;
    private IReadOnlyList<IdeDiagnostic> _diagnostics = [];

    public DiagnosticSquiggles(TextDocument document) => _document = document;

    /// <summary>Painted above the selection so the marks stay visible.</summary>
    public KnownLayer Layer => KnownLayer.Selection;

    public void SetDiagnostics(IReadOnlyList<IdeDiagnostic> diagnostics) =>
        _diagnostics = diagnostics;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_diagnostics.Count == 0) return;

        textView.EnsureVisualLines();

        foreach (var diagnostic in _diagnostics)
        {
            if (diagnostic.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
                continue;

            var segment = SegmentFor(diagnostic);
            if (segment is null) continue;

            var brush = diagnostic.Severity == DiagnosticSeverity.Error
                ? ErrorBrush
                : WarningBrush;

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                DrawWave(drawingContext, brush, rect);
        }
    }

    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#E51400"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#BF8803"));

    /// <summary>
    /// Maps a diagnostic's one-based line and column onto a document segment.
    ///
    /// Roslyn reports a position rather than a length here, so the underline
    /// spans the word starting at that position — enough to point at the
    /// offending token without guessing at its extent.
    /// </summary>
    private ISegment? SegmentFor(IdeDiagnostic diagnostic)
    {
        if (diagnostic.Line < 1 || diagnostic.Line > _document.LineCount) return null;

        var line = _document.GetLineByNumber(diagnostic.Line);
        var column = Math.Clamp(diagnostic.Column - 1, 0, Math.Max(0, line.Length));
        var start = line.Offset + column;

        var text = _document.GetText(line.Offset, line.Length);
        var end = column;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;

        // A diagnostic pointing at punctuation has no word to underline: mark a
        // single character so it is still visible.
        var length = Math.Max(1, end - column);
        length = Math.Min(length, Math.Max(1, line.Length - column));

        return new TextSegment { StartOffset = start, Length = length };
    }

    /// <summary>Draws the wave along the bottom edge of a rectangle.</summary>
    private static void DrawWave(DrawingContext context, IBrush brush, Rect rect)
    {
        const double period = 4;
        const double amplitude = 1.4;

        var y = rect.Bottom - amplitude;
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(rect.Left, y), isFilled: false);

            var up = false;
            for (var x = rect.Left; x < rect.Right; x += period / 2)
            {
                ctx.LineTo(new Point(
                    Math.Min(x + period / 2, rect.Right),
                    up ? y - amplitude : y + amplitude));
                up = !up;
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(brush, 1), geometry);
    }
}
