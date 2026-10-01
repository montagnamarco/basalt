using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>
/// An end for a line drawn from a path: an arrow head, a dot, a bar.
/// </summary>
/// <remarks>
/// The paths are in the cap's own space, measured in pen widths: the origin is
/// the end of the line and +y points away from the line, so a cap whose tip
/// is at (0, 0) and whose base is at y = −h sits on the line's end and points
/// past it. BaseInset is how many pen widths the line stops short of its end,
/// so that a filled head is not crossed by the line under it.
/// </remarks>
public class CustomLineCap : MarshalByRefObject, ICloneable, IDisposable
{
    private GraphicsPath? _fill;
    private GraphicsPath? _stroke;
    private LineCap _startCap = LineCap.Flat;
    private LineCap _endCap = LineCap.Flat;

    public CustomLineCap(GraphicsPath? fillPath, GraphicsPath? strokePath)
        : this(fillPath, strokePath, LineCap.Flat)
    {
    }

    public CustomLineCap(GraphicsPath? fillPath, GraphicsPath? strokePath, LineCap baseCap)
        : this(fillPath, strokePath, baseCap, 0f)
    {
    }

    public CustomLineCap(GraphicsPath? fillPath, GraphicsPath? strokePath, LineCap baseCap, float baseInset)
    {
        // GDI+ takes one or the other. Given both it answers NotImplemented,
        // which System.Drawing turns into this exception; given neither, an
        // invalid parameter.
        if (fillPath is not null && strokePath is not null) throw new NotImplementedException("A custom line cap takes a fill path or a stroke path, not both.");
        if (fillPath is null && strokePath is null) throw new ArgumentException("Parameter is not valid.");

        _fill = (GraphicsPath?)fillPath?.Clone();
        _stroke = (GraphicsPath?)strokePath?.Clone();
        BaseCap = baseCap;
        BaseInset = baseInset;
    }

    /// <summary>For AdjustableArrowCap, which builds its own path.</summary>
    private protected CustomLineCap()
    {
    }

    ~CustomLineCap() => Dispose(false);

    public LineCap BaseCap { get; set; }

    public float BaseInset { get; set; }

    public LineJoin StrokeJoin { get; set; } = LineJoin.Miter;

    public float WidthScale { get; set; } = 1f;

    public void SetStrokeCaps(LineCap startCap, LineCap endCap)
    {
        _startCap = startCap;
        _endCap = endCap;
    }

    public void GetStrokeCaps(out LineCap startCap, out LineCap endCap)
    {
        startCap = _startCap;
        endCap = _endCap;
    }

    // Not virtual, as in GDI+: MemberwiseClone already copies the runtime type,
    // AdjustableArrowCap included.
    public object Clone()
    {
        var clone = (CustomLineCap)MemberwiseClone();
        clone._fill = (GraphicsPath?)_fill?.Clone();
        clone._stroke = (GraphicsPath?)_stroke?.Clone();

        return clone;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        _fill?.Dispose();
        _stroke?.Dispose();
    }

    /// <summary>The area this cap covers, in the cap's space, for a pen of this width.</summary>
    internal virtual SKPath Area(float penWidth)
    {
        if (_fill is not null) return _fill.ToSkia();

        // The stroke path is drawn with the pen, which in the cap's space is
        // one unit wide.
        using var outline = _stroke!.ToSkia();
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f / Math.Max(WidthScale, float.Epsilon),
            StrokeJoin = StrokeJoin switch { LineJoin.Round => SKStrokeJoin.Round, LineJoin.Bevel => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter },
            StrokeCap = _startCap switch { LineCap.Round => SKStrokeCap.Round, LineCap.Square => SKStrokeCap.Square, _ => SKStrokeCap.Butt },
        };

        var area = new SKPath();
        paint.GetFillPath(outline, area);

        return area;
    }
}
