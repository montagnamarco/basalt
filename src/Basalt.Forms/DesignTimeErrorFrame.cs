using SkiaSharp;

namespace System.Windows.Forms;

/// <summary>
/// Told when a control being designed fails to paint. The designer finds it on the control's
/// site, through <see cref="ISite.GetService"/>, the way WinForms designers offer their services,
/// so this library never refers to the IDE. Internal: the IDE reaches it through InternalsVisibleTo,
/// and the public surface does not change.
/// </summary>
/// <remarks>
/// The failure is still contained on the control (a frame is drawn and the next paint tries again).
/// This call only says it happened, so the designer can write it to Output. It is called once per
/// failing paint, after the frame is recorded; an exception it throws reaches the caller of the paint.
/// </remarks>
internal interface IDesignTimePaintFailureSink
{
    /// <summary><paramref name="control"/>'s paint threw <paramref name="exception"/>, and a frame stands in for it.</summary>
    void PaintFailed(Control control, Exception exception);
}

/// <summary>
/// The frame drawn in place of a control whose painting threw while it was being designed. It is
/// what Visual Studio's designer shows for a control it cannot paint: a white area, a red border, a
/// red X from corner to corner, and the exception's type and message as text.
/// </summary>
internal static class DesignTimeErrorFrame
{
    private const float Margin = 4;
    private const float TextSize = 12;

    /// <summary>Draws the frame over <paramref name="bounds"/>, the whole client area of the control.</summary>
    /// <remarks>
    /// The X and the border are drawn without antialiasing, so they are the exact red a pixel test
    /// reads. The text sits on a white box, so it stays readable where the X crosses it.
    /// </remarks>
    internal static void Draw(SKCanvas canvas, SKRect bounds, Exception exception)
    {
        using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawRect(bounds, white);

        using var red = new SKPaint
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = false,
        };
        canvas.DrawLine(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, red);
        canvas.DrawLine(bounds.Right, bounds.Top, bounds.Left, bounds.Bottom, red);
        canvas.DrawRect(SKRect.Inflate(bounds, -0.5f, -0.5f), red);

        DrawText(canvas, bounds, exception);
    }

    private static void DrawText(SKCanvas canvas, SKRect bounds, Exception exception)
    {
        using var font = new SKFont(SKTypeface.Default, TextSize);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };

        var type = exception.GetType().FullName ?? exception.GetType().Name;
        var maxWidth = bounds.Width - 4 * Margin;
        var lines = new List<string> { type };
        lines.AddRange(Wrap(exception.Message, font, maxWidth));

        var width = lines.Max(line => font.MeasureText(line)) + 2 * Margin;
        var height = lines.Count * font.Spacing + 2 * Margin;
        var box = new SKRect(bounds.Left + Margin, bounds.Top + Margin, bounds.Left + Margin + width, bounds.Top + Margin + height);

        canvas.Save();
        canvas.ClipRect(bounds);
        canvas.DrawRect(box, white);

        // The baseline of the first line sits one ascent below the top of the box.
        var baseline = box.Top + Margin - font.Metrics.Ascent;
        foreach (var line in lines)
        {
            canvas.DrawText(line, box.Left + Margin, baseline, font, ink);
            baseline += font.Spacing;
        }

        canvas.Restore();
    }

    /// <summary>Greedy word wrap; a word wider than the line is kept whole and clipped by the caller.</summary>
    private static IEnumerable<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var line = string.Empty;
        foreach (var word in text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureText(candidate) > maxWidth)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0) yield return line;
    }
}
