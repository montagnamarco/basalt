using System.Text;
using SkiaSharp;

namespace System.Drawing.Imaging.Metafiles;

// Playing a metafile back onto a Skia canvas.
//
// The shared half: device-context state, the pen/brush/font to SKPaint mapping, and the drawing
// helpers. The format-specific halves are the record switches in EmfPlayer and WmfPlayer, which
// decode their own record layouts and then call in here.
//
// The guiding rule throughout is that an unrecognised or malformed record is skipped rather than
// thrown on. A metafile routinely carries records from a producer nobody has heard of, and a
// clipboard metafile is routinely truncated; drawing everything that did parse is far more useful
// than discarding a picture because one record in three hundred was unfamiliar. Skipped record
// types are collected rather than just counted, so a caller can see exactly what was not drawn.
//
// Supported: coordinate space (window/viewport origin and extent, map mode, world transform),
// save/restore, pens, brushes (solid and hatched), fonts, lines, polylines/polygons (including
// 16-bit and "poly-poly" forms), Bezier curves, rectangles, ellipses, round rectangles, arcs,
// chords, pies, paths (begin/end/close/fill/stroke/clip), clipping rectangles and text. Not
// supported: bitmap records (BitBlt, StretchBlt, StretchDIBits, AlphaBlend and the WMF DIB
// records) and regions, which are recorded as skipped rather than decoded.
//
// Ported from Majorsilence.Forms.Drawing.Common
// (Majorsilence.Forms.Drawing.Common/Metafiles/MetafileState.cs and MetafilePlayer.cs), MIT
// licensed, copyright Jonathan Pobst and Peter Gill (Majorsilence). Adapted to this assembly's
// namespace and style, and to a List<EmfPlusRecordType> sink instead of a bare counter.

/// <summary>A pen selected into a device context.</summary>
internal sealed class MetaPen
{
    internal SKColor Color = SKColors.Black;
    internal float Width = 1f;
    internal int Style;

    /// <summary>Whether this pen draws nothing (PS_NULL).</summary>
    internal bool IsNull => Style == 5;

    /// <summary>The dash pattern for this pen's style, or null for a solid line.</summary>
    internal float[]? DashPattern
    {
        get
        {
            var unit = Math.Max(Width, 1f);

            return Style switch
            {
                1 => [unit * 6, unit * 3],
                2 => [unit, unit * 2],
                3 => [unit * 6, unit * 2, unit, unit * 2],
                4 => [unit * 6, unit * 2, unit, unit * 2, unit, unit * 2],
                _ => null,
            };
        }
    }
}

/// <summary>A brush selected into a device context.</summary>
internal sealed class MetaBrush
{
    internal SKColor Color = SKColors.White;
    internal int Style;
    internal int Hatch;

    /// <summary>Whether this brush fills nothing (BS_NULL / BS_HOLLOW).</summary>
    internal bool IsNull => Style == 1;
}

/// <summary>A font selected into a device context.</summary>
internal sealed class MetaFont
{
    internal string Name = "Arial";
    internal float Height = 12f;
    internal bool Bold;
    internal bool Italic;
    internal bool Underline;
    internal bool Strikeout;
    internal int Escapement;
}

/// <summary>The part of a device context that SaveDC and RestoreDC preserve.</summary>
internal sealed class MetaDeviceContext
{
    internal MetaPen Pen = new();
    internal MetaBrush Brush = new();
    internal MetaFont Font = new();

    internal SKColor TextColor = SKColors.Black;
    internal SKColor BackColor = SKColors.White;
    internal int BackMode = 2;
    internal int TextAlign;
    internal int PolyFillMode = 1;
    internal int MapMode = 1;

    internal SKPoint Current;
    internal SKPoint WindowOrigin;
    internal SKSize WindowExtent = new(1, 1);
    internal SKPoint ViewportOrigin;
    internal SKSize ViewportExtent = new(1, 1);
    internal bool ExtentsSet;

    internal SKMatrix World = SKMatrix.Identity;

    internal MetaDeviceContext Clone() => new()
    {
        Pen = Pen,
        Brush = Brush,
        Font = Font,
        TextColor = TextColor,
        BackColor = BackColor,
        BackMode = BackMode,
        TextAlign = TextAlign,
        PolyFillMode = PolyFillMode,
        MapMode = MapMode,
        Current = Current,
        WindowOrigin = WindowOrigin,
        WindowExtent = WindowExtent,
        ViewportOrigin = ViewportOrigin,
        ViewportExtent = ViewportExtent,
        ExtentsSet = ExtentsSet,
        World = World,
    };

    /// <summary>The logical-to-page transform implied by the map mode and window/viewport pair.</summary>
    internal SKMatrix MapTransform
    {
        get
        {
            var (scale, flip) = MapMode switch
            {
                2 => (96f / 254f, true),
                3 => (96f / 2540f, true),
                4 => (96f / 100f, true),
                5 => (96f / 1000f, true),
                6 => (96f / 1440f, true),
                _ => (1f, false),
            };

            if (flip) return SKMatrix.CreateScale(scale, -scale);

            if (!ExtentsSet || WindowExtent.Width == 0 || WindowExtent.Height == 0) return SKMatrix.Identity;

            var sx = ViewportExtent.Width / WindowExtent.Width;
            var sy = ViewportExtent.Height / WindowExtent.Height;

            return SKMatrix.CreateTranslation(ViewportOrigin.X, ViewportOrigin.Y)
                .PreConcat(SKMatrix.CreateScale(sx, sy))
                .PreConcat(SKMatrix.CreateTranslation(-WindowOrigin.X, -WindowOrigin.Y));
        }
    }
}

/// <summary>The GDI stock objects a metafile can select without creating them.</summary>
internal static class StockObjects
{
    internal static MetaBrush? Brush(int index) => index switch
    {
        0 => new MetaBrush { Color = SKColors.White },
        1 => new MetaBrush { Color = new SKColor(0xC0, 0xC0, 0xC0) },
        2 => new MetaBrush { Color = new SKColor(0x80, 0x80, 0x80) },
        3 => new MetaBrush { Color = new SKColor(0x40, 0x40, 0x40) },
        4 => new MetaBrush { Color = SKColors.Black },
        5 => new MetaBrush { Style = 1 },
        _ => null,
    };

    internal static MetaPen? Pen(int index) => index switch
    {
        6 => new MetaPen { Color = SKColors.White },
        7 => new MetaPen { Color = SKColors.Black },
        8 => new MetaPen { Style = 5 },
        _ => null,
    };

    internal static MetaFont? Font(int index) => index switch
    {
        10 or 11 or 16 => new MetaFont { Name = "Courier New", Height = 12f },
        12 or 13 or 17 => new MetaFont { Name = "Arial", Height = 12f },
        _ => null,
    };
}

/// <summary>Replays a metafile's records onto a canvas.</summary>
internal abstract class MetafilePlayer
{
    private readonly Stack<MetaDeviceContext> _saved = new();
    private readonly Dictionary<int, object> _objects = [];
    private readonly SKMatrix _output;
    private readonly List<EmfPlusRecordType> _unsupported;

    protected MetafilePlayer(SKCanvas canvas, SKMatrix output, SKSize deviceExtent, List<EmfPlusRecordType> unsupported)
    {
        Canvas = canvas;
        _output = output;
        _unsupported = unsupported;

        Dc = new MetaDeviceContext
        {
            ViewportExtent = deviceExtent.Width > 0 && deviceExtent.Height > 0 ? deviceExtent : new SKSize(1, 1),
        };
    }

    protected MetaDeviceContext Dc { get; private set; }

    protected SKCanvas Canvas { get; }

    protected SKPath? RecordingPath { get; set; }

    protected SKPath? CompletedPath { get; set; }

    /// <summary>Converts a raw, format-specific record code to the enumeration GDI+ uses.</summary>
    protected abstract EmfPlusRecordType ToRecordType(int raw);

    /// <summary>Notes that a record was recognised but not acted on.</summary>
    protected void Skip(int raw) => _unsupported.Add(ToRecordType(raw));

    protected void SaveDc()
    {
        _saved.Push(Dc.Clone());
        Canvas.Save();
    }

    protected void RestoreDc(int relative)
    {
        var levels = relative < 0 ? -relative : Math.Max(_saved.Count - relative, 1);

        for (var i = 0; i < levels && _saved.Count > 0; i++)
        {
            Dc = _saved.Pop();
            Canvas.Restore();
        }
    }

    protected void StoreObject(int handle, object value) => _objects[handle] = value;

    protected void DeleteObject(int handle) => _objects.Remove(handle);

    protected void SelectObject(int handle)
    {
        if ((handle & 0x80000000) != 0)
        {
            var index = handle & 0x7FFFFFFF;

            if (StockObjects.Pen(index) is { } stockPen) Dc.Pen = stockPen;
            else if (StockObjects.Brush(index) is { } stockBrush) Dc.Brush = stockBrush;
            else if (StockObjects.Font(index) is { } stockFont) Dc.Font = stockFont;

            return;
        }

        if (!_objects.TryGetValue(handle, out var value)) return;

        switch (value)
        {
            case MetaPen pen: Dc.Pen = pen; break;
            case MetaBrush brush: Dc.Brush = brush; break;
            case MetaFont font: Dc.Font = font; break;
        }
    }

    protected void ApplyTransform() => Canvas.SetMatrix(_output.PreConcat(Dc.MapTransform).PreConcat(Dc.World));

    protected SKPaint? StrokePaint()
    {
        if (Dc.Pen.IsNull) return null;

        var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = Dc.Pen.Color,
            StrokeWidth = Dc.Pen.Width,
            IsAntialias = true,
        };

        if (Dc.Pen.DashPattern is { } dashes) paint.PathEffect = SKPathEffect.CreateDash(dashes, 0);

        return paint;
    }

    protected SKPaint? FillPaint()
    {
        if (Dc.Brush.IsNull) return null;

        var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = Dc.Brush.Color,
            IsAntialias = true,
        };

        if (Dc.Brush.Style == 2) paint.Shader = HatchShader(Dc.Brush.Hatch, Dc.Brush.Color, Dc.BackColor, Dc.BackMode == 2);

        return paint;
    }

    private static SKShader HatchShader(int hatch, SKColor foreground, SKColor background, bool opaque)
    {
        const int cell = 8;

        using var bitmap = new SKBitmap(cell, cell, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = new SKCanvas(bitmap);

        surface.Clear(opaque ? background : SKColors.Transparent);

        using var pen = new SKPaint { Color = foreground, StrokeWidth = 1, Style = SKPaintStyle.Stroke };

        switch (hatch)
        {
            case 0:
                surface.DrawLine(0, cell / 2f, cell, cell / 2f, pen);
                break;
            case 1:
                surface.DrawLine(cell / 2f, 0, cell / 2f, cell, pen);
                break;
            case 2:
                surface.DrawLine(0, 0, cell, cell, pen);
                break;
            case 3:
                surface.DrawLine(0, cell, cell, 0, pen);
                break;
            case 4:
                surface.DrawLine(0, cell / 2f, cell, cell / 2f, pen);
                surface.DrawLine(cell / 2f, 0, cell / 2f, cell, pen);
                break;
            default:
                surface.DrawLine(0, 0, cell, cell, pen);
                surface.DrawLine(0, cell, cell, 0, pen);
                break;
        }

        return SKShader.CreateBitmap(bitmap.Copy(), SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
    }

    /// <summary>Fills a closed shape with the brush, then outlines it with the pen.</summary>
    protected void FillThenStroke(SKPath path)
    {
        ApplyTransform();
        path.FillType = Dc.PolyFillMode == 2 ? SKPathFillType.Winding : SKPathFillType.EvenOdd;

        using var fill = FillPaint();
        if (fill is not null) Canvas.DrawPath(path, fill);

        using var stroke = StrokePaint();
        if (stroke is not null) Canvas.DrawPath(path, stroke);
    }

    protected void StrokeOnly(SKPath path)
    {
        ApplyTransform();

        using var stroke = StrokePaint();
        if (stroke is not null) Canvas.DrawPath(path, stroke);
    }

    protected void DrawText(float x, float y, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        ApplyTransform();

        var style = SKFontStyle.Normal;

        if (Dc.Font.Bold && Dc.Font.Italic) style = SKFontStyle.BoldItalic;
        else if (Dc.Font.Bold) style = SKFontStyle.Bold;
        else if (Dc.Font.Italic) style = SKFontStyle.Italic;

        using var typeface = SKTypeface.FromFamilyName(Dc.Font.Name, style) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, Math.Abs(Dc.Font.Height));
        using var paint = new SKPaint { Color = Dc.TextColor, IsAntialias = true };

        var width = font.MeasureText(text);

        var horizontal = Dc.TextAlign & 6;
        var left = horizontal switch
        {
            6 => x - (width / 2f),
            2 => x - width,
            _ => x,
        };

        var metrics = font.Metrics;
        var vertical = Dc.TextAlign & 24;
        var baseline = vertical switch
        {
            24 => y,
            8 => y + metrics.Descent,
            _ => y - metrics.Ascent,
        };

        if (Dc.BackMode == 2)
        {
            using var back = new SKPaint { Color = Dc.BackColor, Style = SKPaintStyle.Fill };
            Canvas.DrawRect(left, baseline + metrics.Ascent, width, metrics.Descent - metrics.Ascent, back);
        }

        if (Dc.Font.Escapement != 0)
        {
            Canvas.Save();
            Canvas.RotateDegrees(-Dc.Font.Escapement / 10f, x, y);
            Canvas.DrawText(text, left, baseline, font, paint);
            Canvas.Restore();
            return;
        }

        Canvas.DrawText(text, left, baseline, font, paint);

        if (Dc.Font.Underline || Dc.Font.Strikeout)
        {
            using var rule = new SKPaint { Color = Dc.TextColor, StrokeWidth = Math.Max(1f, Dc.Font.Height / 14f) };

            if (Dc.Font.Underline) Canvas.DrawLine(left, baseline + 1, left + width, baseline + 1, rule);
            if (Dc.Font.Strikeout) Canvas.DrawLine(left, baseline + (metrics.Ascent / 3f), left + width, baseline + (metrics.Ascent / 3f), rule);
        }
    }

    protected static SKPath BuildPath(SKPoint[] points, bool close)
    {
        var path = new SKPath();

        if (points.Length == 0) return path;

        path.MoveTo(points[0]);

        for (var i = 1; i < points.Length; i++) path.LineTo(points[i]);

        if (close) path.Close();

        return path;
    }

    protected static SKPath BuildBezier(SKPoint[] points, SKPoint? start)
    {
        var path = new SKPath();
        var index = 0;

        if (start is { } from) path.MoveTo(from);
        else if (points.Length > 0) path.MoveTo(points[index++]);

        for (; index + 2 < points.Length; index += 3) path.CubicTo(points[index], points[index + 1], points[index + 2]);

        return path;
    }

    protected static void AddArc(SKPath path, SKRect box, SKPoint from, SKPoint to, ArcKind kind)
    {
        var centre = new SKPoint(box.MidX, box.MidY);
        var startAngle = Degrees(centre, from);
        var sweep = Degrees(centre, to) - startAngle;

        if (sweep <= 0) sweep += 360f;

        switch (kind)
        {
            case ArcKind.Pie:
                path.MoveTo(centre);
                path.ArcTo(box, startAngle, sweep, forceMoveTo: false);
                path.Close();
                break;
            case ArcKind.Chord:
                path.AddArc(box, startAngle, sweep);
                path.Close();
                break;
            default:
                path.AddArc(box, startAngle, sweep);
                break;
        }
    }

    private static float Degrees(SKPoint centre, SKPoint point)
    {
        var angle = (float)(Math.Atan2(point.Y - centre.Y, point.X - centre.X) * 180 / Math.PI);
        return angle < 0 ? angle + 360f : angle;
    }

    protected enum ArcKind
    {
        Arc,
        Chord,
        Pie,
    }

    protected static SKColor ColorRef(uint value) =>
        new((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF), 0xFF);

    protected static string ReadName(byte[] data, int offset, int maxChars)
    {
        if (offset < 0 || offset >= data.Length) return string.Empty;

        var builder = new StringBuilder();

        for (var i = 0; i < maxChars; i++)
        {
            var at = offset + (i * 2);

            if (at + 1 >= data.Length) break;

            var ch = (char)BitConverter.ToUInt16(data, at);

            if (ch == '\0') break;

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
