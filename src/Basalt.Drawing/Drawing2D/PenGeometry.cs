using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>What a pen covers, for the questions a path asks without drawing.</summary>
internal static class PenGeometry
{
    /// <summary>A stroke paint with the pen's geometry; colour and blending do not matter here.</summary>
    public static SKPaint StrokePaint(Pen pen) => new()
    {
        Style = SKPaintStyle.Stroke,

        // A pen thinner than a pixel still covers one.
        StrokeWidth = Math.Max(pen.Width, 1f),
        StrokeCap = SKStrokeCap.Butt,
        StrokeJoin = SKStrokeJoin.Miter,
    };
}
