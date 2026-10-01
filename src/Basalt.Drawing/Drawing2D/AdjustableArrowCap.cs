using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>An arrow head of a given width and height, filled or as two strokes.</summary>
/// <remarks>
/// Width and Height are in pen widths. The tip is at the end of the line; the
/// two back corners are Height behind it and Width apart. MiddleInset moves
/// the middle of the back edge towards the tip, giving a swallow-tailed head.
/// </remarks>
public sealed class AdjustableArrowCap : CustomLineCap
{
    public AdjustableArrowCap(float width, float height)
        : this(width, height, true)
    {
    }

    public AdjustableArrowCap(float width, float height, bool isFilled)
    {
        Width = width;
        Height = height;
        Filled = isFilled;
    }

    public float Width { get; set; }

    public float Height { get; set; }

    public float MiddleInset { get; set; }

    public bool Filled { get; set; }

    /// <summary>
    /// The line stops at the back of a filled head, not at its tip, so it does
    /// not show through; an open head sits on the line's end.
    /// </summary>
    internal float EffectiveInset => Filled ? Height - MiddleInset : 0f;

    internal override SKPath Area(float penWidth)
    {
        var head = new SKPath();

        if (Filled)
        {
            head.MoveTo(0, 0);
            head.LineTo(Width / 2, -Height);
            head.LineTo(0, -Height + MiddleInset);
            head.LineTo(-Width / 2, -Height);
            head.Close();

            return head;
        }

        head.MoveTo(-Width / 2, -Height);
        head.LineTo(0, 0);
        head.LineTo(Width / 2, -Height);

        // The two strokes of an open head are drawn with the pen itself.
        // One pen width once placed, whatever WidthScale enlarges the head by.
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1f / Math.Max(WidthScale, float.Epsilon), StrokeJoin = SKStrokeJoin.Miter };
        var area = new SKPath();
        paint.GetFillPath(head, area);
        head.Dispose();

        return area;
    }
}
