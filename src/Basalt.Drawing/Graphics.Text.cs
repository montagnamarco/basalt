using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SkiaSharp;

namespace System.Drawing;

/// <summary>Drawing and measuring strings, through the one layout both use.</summary>
public sealed partial class Graphics
{
    /// <summary>How many device pixels one unit of the page is: the page unit times the page scale.</summary>
    internal float PageUnitsToPixels => PixelsPerUnit(_pageUnit) * _pageScale;

    public void DrawString(string? s, Font font, Brush brush, float x, float y) => DrawString(s, font, brush, new RectangleF(x, y, 0, 0), null);

    public void DrawString(string? s, Font font, Brush brush, float x, float y, StringFormat? format) => DrawString(s, font, brush, new RectangleF(x, y, 0, 0), format);

    public void DrawString(string? s, Font font, Brush brush, PointF point) => DrawString(s, font, brush, new RectangleF(point.X, point.Y, 0, 0), null);

    public void DrawString(string? s, Font font, Brush brush, PointF point, StringFormat? format) => DrawString(s, font, brush, new RectangleF(point.X, point.Y, 0, 0), format);

    public void DrawString(string? s, Font font, Brush brush, RectangleF layoutRectangle) => DrawString(s, font, brush, layoutRectangle, null);

    public void DrawString(ReadOnlySpan<char> s, Font font, Brush brush, float x, float y) => DrawString(s.ToString(), font, brush, x, y);

    public void DrawString(ReadOnlySpan<char> s, Font font, Brush brush, float x, float y, StringFormat? format) => DrawString(s.ToString(), font, brush, x, y, format);

    public void DrawString(ReadOnlySpan<char> s, Font font, Brush brush, PointF point) => DrawString(s.ToString(), font, brush, point);

    public void DrawString(ReadOnlySpan<char> s, Font font, Brush brush, PointF point, StringFormat? format) => DrawString(s.ToString(), font, brush, point, format);

    public void DrawString(ReadOnlySpan<char> s, Font font, Brush brush, RectangleF layoutRectangle) => DrawString(s.ToString(), font, brush, layoutRectangle);

    public void DrawString(ReadOnlySpan<char> s, Font font, Brush brush, RectangleF layoutRectangle, StringFormat? format) => DrawString(s.ToString(), font, brush, layoutRectangle, format);

    /// <summary>
    /// Draws the string laid out in the rectangle (or from its corner when it
    /// has no size), clipped to it unless the format says NoClip.
    /// </summary>
    public void DrawString(string? s, Font font, Brush brush, RectangleF layoutRectangle, StringFormat? format)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(brush);

        if (string.IsNullOrEmpty(s)) return;

        using var layout = TextLayout.Create(s, font, EmSizeInWorldUnits(font), layoutRectangle, format);
        using var paint = Fill(brush);
        paint.IsAntialias = TextRenderingHint is not (TextRenderingHint.SingleBitPerPixel or TextRenderingHint.SingleBitPerPixelGridFit);

        using var aligned = AlignToPixels();

        var clip = layoutRectangle.Width > 0 && layoutRectangle.Height > 0 && (format?.FormatFlags & StringFormatFlags.NoClip) == 0;
        if (clip) _canvas.ClipRect(new SKRect(layoutRectangle.Left, layoutRectangle.Top, layoutRectangle.Right, layoutRectangle.Bottom), antialias: paint.IsAntialias);

        foreach (var line in layout.Lines)
        {
            var baseline = line.Top + layout.Ascent;

            foreach (var (text, skFont, offset) in layout.Runs(line))
            {
                skFont.Edging = TextRenderingHint switch
                {
                    TextRenderingHint.SingleBitPerPixel or TextRenderingHint.SingleBitPerPixelGridFit => SKFontEdging.Alias,
                    TextRenderingHint.ClearTypeGridFit => SKFontEdging.SubpixelAntialias,
                    _ => SKFontEdging.Antialias,
                };

                _canvas.DrawText(text, line.X + offset, baseline, SKTextAlign.Left, skFont, paint);
            }

            DrawDecorations(layout, line, baseline, paint);
        }
    }

    public SizeF MeasureString(string? text, Font font) => MeasureString(text, font, new SizeF(0, 0), null);

    public SizeF MeasureString(string? text, Font font, int width) => MeasureString(text, font, new SizeF(width, 0), null);

    public SizeF MeasureString(string? text, Font font, int width, StringFormat? format) => MeasureString(text, font, new SizeF(width, 0), format);

    public SizeF MeasureString(string? text, Font font, SizeF layoutArea) => MeasureString(text, font, layoutArea, null);

    public SizeF MeasureString(string? text, Font font, PointF origin, StringFormat? stringFormat) => MeasureString(text, font, new SizeF(0, 0), stringFormat);

    public SizeF MeasureString(string? text, Font font, SizeF layoutArea, StringFormat? stringFormat) =>
        MeasureString(text, font, layoutArea, stringFormat, out _, out _);

    public SizeF MeasureString(string? text, Font font, SizeF layoutArea, StringFormat? stringFormat, out int charactersFitted, out int linesFilled) =>
        MeasureStringInternal(text.AsSpan(), font, new RectangleF(PointF.Empty, layoutArea), stringFormat, out charactersFitted, out linesFilled);

    public SizeF MeasureString(ReadOnlySpan<char> text, Font font) => MeasureString(text.ToString(), font);

    public SizeF MeasureString(ReadOnlySpan<char> text, Font font, int width) => MeasureString(text.ToString(), font, width);

    public SizeF MeasureString(ReadOnlySpan<char> text, Font font, int width, StringFormat? format) => MeasureString(text.ToString(), font, width, format);

    public SizeF MeasureString(ReadOnlySpan<char> text, Font font, SizeF layoutArea) => MeasureString(text.ToString(), font, layoutArea);

    public SizeF MeasureString(ReadOnlySpan<char> text, Font font, PointF origin, StringFormat? stringFormat) => MeasureString(text.ToString(), font, origin, stringFormat);

    public SizeF MeasureString(ReadOnlySpan<char> text, Font font, SizeF layoutArea, StringFormat? stringFormat) => MeasureString(text.ToString(), font, layoutArea, stringFormat);

    public SizeF MeasureString(ReadOnlySpan<char> text, Font font, SizeF layoutArea, StringFormat? stringFormat, out int charactersFitted, out int linesFilled) =>
        MeasureString(text.ToString(), font, layoutArea, stringFormat, out charactersFitted, out linesFilled);

    /// <summary>
    /// The size the string takes laid out in the rectangle, how many of its
    /// characters show and on how many lines: what MeasureString answers.
    /// </summary>
    public SizeF MeasureStringInternal(ReadOnlySpan<char> text, Font font, RectangleF layoutArea, StringFormat? stringFormat, out int charactersFitted, out int linesFilled)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (text.IsEmpty)
        {
            charactersFitted = 0;
            linesFilled = 0;

            return SizeF.Empty;
        }

        using var layout = TextLayout.Create(text.ToString(), font, EmSizeInWorldUnits(font), layoutArea, stringFormat);
        charactersFitted = layout.CharactersFitted;
        linesFilled = layout.LinesFilled;

        return layout.Size;
    }

    public Region[] MeasureCharacterRanges(ReadOnlySpan<char> text, Font font, RectangleF layoutRect, StringFormat? stringFormat) =>
        MeasureCharacterRanges(text.ToString(), font, layoutRect, stringFormat);

    /// <summary>
    /// For each range set on the format, the region its characters occupy:
    /// one rectangle per line it appears on, a line high.
    /// </summary>
    public Region[] MeasureCharacterRanges(string? text, Font font, RectangleF layoutRect, StringFormat? stringFormat)
    {
        ArgumentNullException.ThrowIfNull(font);

        var ranges = stringFormat?.MeasurableCharacterRanges ?? [];
        if (string.IsNullOrEmpty(text) || ranges.Length == 0) return [];

        using var layout = TextLayout.Create(text, font, EmSizeInWorldUnits(font), layoutRect, stringFormat);
        var regions = new Region[ranges.Length];

        for (var r = 0; r < ranges.Length; r++)
        {
            var first = ranges[r].First;
            var last = ranges[r].First + ranges[r].Length;
            var region = new Region(RectangleF.Empty);

            foreach (var line in layout.Lines)
            {
                var start = -1;
                var end = -1;

                for (var k = 0; k < line.Sources.Length; k++)
                {
                    if (line.Sources[k] < first || line.Sources[k] >= last) continue;

                    if (start < 0) start = k;
                    end = k + 1;
                }

                if (start < 0) continue;

                var left = line.X + layout.Measure(line.Text[..start]);
                var right = line.X + layout.Measure(line.Text[..end]);
                region.Union(RectangleF.FromLTRB(left, line.Top, right, line.Top + layout.LineHeight));
            }

            regions[r] = region;
        }

        return regions;
    }

    /// <summary>
    /// The font's em size in the units the drawing is given in: its own when
    /// it is in world units, otherwise its physical size at 96 dpi, divided by
    /// the page's unit and scale as GDI+ does.
    /// </summary>
    private float EmSizeInWorldUnits(Font font) =>
        font.Unit == GraphicsUnit.World ? font.Size : font.EmSizeInPixels(DpiY) / PageUnitsToPixels;

    /// <summary>Underline, strikeout and the underline of a shown hotkey, at the font's own positions.</summary>
    private void DrawDecorations(TextLayout layout, TextLayout.Line line, float baseline, SKPaint paint)
    {
        using var font = layout.Font.ToSkia(layout.EmSize);
        var metrics = font.Metrics;
        var thickness = Math.Max(metrics.UnderlineThickness ?? layout.EmSize / 14f, layout.EmSize / 20f);

        void Bar(float x, float width, float offset)
        {
            _canvas.DrawRect(new SKRect(x, baseline + offset, x + width, baseline + offset + thickness), paint);
        }

        if (layout.Font.Underline) Bar(line.X, line.Width, metrics.UnderlinePosition ?? layout.EmSize / 10f);
        if (layout.Font.Strikeout) Bar(line.X, line.Width, metrics.StrikeoutPosition ?? -layout.EmSize / 4f);

        var hotkey = Array.IndexOf(line.Sources, layout.HotkeyIndex);
        if (layout.HotkeyIndex >= 0 && hotkey >= 0)
        {
            var left = line.X + layout.Measure(line.Text[..hotkey]);
            var width = layout.Measure(line.Text[hotkey..(hotkey + 1)]);
            Bar(left, width, metrics.UnderlinePosition ?? layout.EmSize / 10f);
        }
    }
}
