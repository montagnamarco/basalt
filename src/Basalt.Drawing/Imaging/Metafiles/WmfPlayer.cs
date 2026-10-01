using SkiaSharp;

namespace System.Drawing.Imaging.Metafiles;

/// <summary>Interprets a Windows metafile's records (MS-WMF).</summary>
/// <remarks>
/// WMF is the 16-bit predecessor of EMF and differs in three ways that matter here: coordinates
/// are 16-bit, several records take their arguments in reverse order, and objects are selected by
/// an index into a table the metafile fills sequentially rather than by a handle the record names.
/// <para>
/// Ported from Majorsilence.Forms.Drawing.Common
/// (Majorsilence.Forms.Drawing.Common/Metafiles/WmfPlayer.cs), MIT licensed, copyright Jonathan
/// Pobst and Peter Gill (Majorsilence). Adapted to this assembly's namespace and style, and with
/// the device-independent bitmap records (StretchDIB, DibStretchBlt) left unsupported.
/// </para>
/// </remarks>
internal sealed class WmfPlayer(SKCanvas canvas, SKMatrix output, SKSize deviceExtent, List<EmfPlusRecordType> unsupported)
    : MetafilePlayer(canvas, output, deviceExtent, unsupported)
{
    private readonly List<object?> _table = [];

    internal void Play(IReadOnlyList<MetafileRecord> records)
    {
        foreach (var record in records)
        {
            try
            {
                Execute(record);
            }
            catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                Skip(record.Type);
            }
        }
    }

    // WMF records, when enumerated, are reported as EmfPlusRecordType.WmfRecordBase plus the raw
    // function code -- which is exactly how GDI+'s own EnumerateMetafile reports them for a WMF.
    protected override EmfPlusRecordType ToRecordType(int raw) => (EmfPlusRecordType)(raw + 0x10000);

    private void Execute(MetafileRecord r)
    {
        switch (r.Type)
        {
            case Meta.Eof:
                break;

            case Meta.SetWindowOrg:
                Dc.WindowOrigin = new SKPoint(r.Int16(2), r.Int16(0));
                Dc.ExtentsSet = true;
                break;
            case Meta.SetWindowExt:
                Dc.WindowExtent = new SKSize(r.Int16(2), r.Int16(0));
                Dc.ExtentsSet = true;
                break;
            case Meta.SetViewportOrg:
                Dc.ViewportOrigin = new SKPoint(r.Int16(2), r.Int16(0));
                Dc.ExtentsSet = true;
                break;
            case Meta.SetViewportExt:
                Dc.ViewportExtent = new SKSize(r.Int16(2), r.Int16(0));
                Dc.ExtentsSet = true;
                break;
            case Meta.OffsetWindowOrg:
                Dc.WindowOrigin = new SKPoint(Dc.WindowOrigin.X + r.Int16(2), Dc.WindowOrigin.Y + r.Int16(0));
                break;
            case Meta.OffsetViewportOrg:
                Dc.ViewportOrigin = new SKPoint(Dc.ViewportOrigin.X + r.Int16(2), Dc.ViewportOrigin.Y + r.Int16(0));
                break;
            case Meta.ScaleWindowExt:
                Dc.WindowExtent = Scale(Dc.WindowExtent, r.Int16(6), r.Int16(4), r.Int16(2), r.Int16(0));
                break;
            case Meta.ScaleViewportExt:
                Dc.ViewportExtent = Scale(Dc.ViewportExtent, r.Int16(6), r.Int16(4), r.Int16(2), r.Int16(0));
                break;
            case Meta.SetMapMode:
                Dc.MapMode = r.Int16(0);
                break;

            case Meta.SaveDC:
                SaveDc();
                break;
            case Meta.RestoreDC:
                RestoreDc(r.Int16(0));
                break;
            case Meta.SetTextColor:
                Dc.TextColor = ColorRef(r.UInt32(0));
                break;
            case Meta.SetBkColor:
                Dc.BackColor = ColorRef(r.UInt32(0));
                break;
            case Meta.SetBkMode:
                Dc.BackMode = r.Int16(0);
                break;
            case Meta.SetTextAlign:
                Dc.TextAlign = r.Int16(0);
                break;
            case Meta.SetPolyFillMode:
                Dc.PolyFillMode = r.Int16(0);
                break;
            case Meta.SetRop2:
            case Meta.SetStretchBltMode:
                Skip(r.Type);
                break;

            case Meta.CreatePenIndirect:
                Add(new MetaPen
                {
                    Style = r.Int16(0),
                    Width = r.Int16(2),
                    Color = ColorRef(r.UInt32(6)),
                });
                break;
            case Meta.CreateBrushIndirect:
                Add(new MetaBrush
                {
                    Style = r.Int16(0),
                    Color = ColorRef(r.UInt32(2)),
                    Hatch = r.Int16(6),
                });
                break;
            case Meta.CreateFontIndirect:
                Add(ReadFont(r));
                break;
            case Meta.SelectObject:
                Select(r.Int16(0));
                break;
            case Meta.DeleteObject:
                Remove(r.Int16(0));
                break;

            case Meta.MoveTo:
                Dc.Current = new SKPoint(r.Int16(2), r.Int16(0));
                break;
            case Meta.LineTo:
                {
                    var to = new SKPoint(r.Int16(2), r.Int16(0));
                    using var path = new SKPath();
                    path.MoveTo(Dc.Current);
                    path.LineTo(to);
                    StrokeOnly(path);
                    Dc.Current = to;
                    break;
                }
            case Meta.Polyline:
            case Meta.Polygon:
                {
                    var points = ReadPoints(r, 2, r.Int16(0));

                    if (points.Length == 0) break;

                    using var path = BuildPath(points, close: r.Type == Meta.Polygon);

                    if (r.Type == Meta.Polygon) FillThenStroke(path);
                    else StrokeOnly(path);

                    break;
                }
            case Meta.PolyPolygon:
                {
                    var polygons = r.Int16(0);

                    if (polygons <= 0) break;

                    using var path = new SKPath();
                    var offset = 2 + (polygons * 2);

                    for (var i = 0; i < polygons; i++)
                    {
                        var count = r.Int16(2 + (i * 2));
                        var points = ReadPoints(r, offset, count);

                        if (points.Length == 0) break;

                        path.MoveTo(points[0]);

                        for (var p = 1; p < points.Length; p++) path.LineTo(points[p]);

                        path.Close();
                        offset += count * 4;
                    }

                    FillThenStroke(path);
                    break;
                }

            case Meta.Rectangle:
                {
                    using var path = new SKPath();
                    path.AddRect(ReadRect(r, 0));
                    FillThenStroke(path);
                    break;
                }
            case Meta.Ellipse:
                {
                    using var path = new SKPath();
                    path.AddOval(ReadRect(r, 0));
                    FillThenStroke(path);
                    break;
                }
            case Meta.RoundRect:
                {
                    using var path = new SKPath();
                    path.AddRoundRect(ReadRect(r, 4), r.Int16(2) / 2f, r.Int16(0) / 2f);
                    FillThenStroke(path);
                    break;
                }
            case Meta.Arc:
            case Meta.Chord:
            case Meta.Pie:
                {
                    var kind = r.Type switch
                    {
                        Meta.Pie => ArcKind.Pie,
                        Meta.Chord => ArcKind.Chord,
                        _ => ArcKind.Arc,
                    };

                    using var path = new SKPath();
                    AddArc(path, ReadRect(r, 8),
                        new SKPoint(r.Int16(6), r.Int16(4)),
                        new SKPoint(r.Int16(2), r.Int16(0)), kind);

                    if (kind == ArcKind.Arc) StrokeOnly(path);
                    else FillThenStroke(path);

                    break;
                }
            case Meta.SetPixel:
                {
                    ApplyTransform();
                    using var paint = new SKPaint { Color = ColorRef(r.UInt32(0)) };
                    Canvas.DrawPoint(r.Int16(6), r.Int16(4), paint);
                    break;
                }

            case Meta.IntersectClipRect:
                ApplyTransform();
                Canvas.ClipRect(ReadRect(r, 0));
                break;

            case Meta.TextOut:
                {
                    var count = r.Int16(0);

                    if (count <= 0) break;

                    var text = global::System.Text.Encoding.Latin1.GetString(r.Data, 2, Math.Min(count, r.Data.Length - 2));
                    var after = 2 + count + (count & 1);

                    DrawText(r.Int16(after + 2), r.Int16(after), text);
                    break;
                }
            case Meta.ExtTextOut:
                {
                    var count = r.Int16(4);
                    var options = r.Int16(6);

                    if (count <= 0) break;

                    var offset = 8 + ((options & 6) != 0 ? 8 : 0);

                    if (offset >= r.Data.Length) break;

                    var text = global::System.Text.Encoding.Latin1.GetString(r.Data, offset, Math.Min(count, r.Data.Length - offset));
                    DrawText(r.Int16(2), r.Int16(0), text);
                    break;
                }

            default:
                Skip(r.Type);
                break;
        }
    }

    // WMF names objects by their index in a table the metafile fills as it creates them; a delete
    // leaves a hole the next create reuses, so appending unconditionally would put every later
    // object one slot out.
    private void Add(object value)
    {
        var hole = _table.IndexOf(null);

        if (hole >= 0) _table[hole] = value;
        else _table.Add(value);
    }

    private void Remove(int index)
    {
        if (index >= 0 && index < _table.Count) _table[index] = null;
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _table.Count) return;

        switch (_table[index])
        {
            case MetaPen pen: Dc.Pen = pen; break;
            case MetaBrush brush: Dc.Brush = brush; break;
            case MetaFont font: Dc.Font = font; break;
        }
    }

    private static SKSize Scale(SKSize extent, int xNum, int xDenom, int yNum, int yDenom) => new(
        xDenom == 0 ? extent.Width : extent.Width * xNum / xDenom,
        yDenom == 0 ? extent.Height : extent.Height * yNum / yDenom);

    private static SKPoint[] ReadPoints(MetafileRecord r, int offset, int count)
    {
        if (count <= 0) return [];

        var available = (r.Data.Length - offset) / 4;
        var points = new SKPoint[Math.Min(count, Math.Max(available, 0))];

        for (var i = 0; i < points.Length; i++) points[i] = new SKPoint(r.Int16(offset + (i * 4)), r.Int16(offset + (i * 4) + 2));

        return points;
    }

    // WMF rectangles are stored bottom, right, top, left -- reversed from EMF's left, top, right,
    // bottom, which is the single most common way to misread a WMF.
    private static SKRect ReadRect(MetafileRecord r, int offset) =>
        SKRect.Create(r.Int16(offset + 6), r.Int16(offset + 4),
            r.Int16(offset + 2) - r.Int16(offset + 6),
            r.Int16(offset) - r.Int16(offset + 4)).Standardized;

    private static MetaFont ReadFont(MetafileRecord r)
    {
        var height = Math.Abs(r.Int16(0));
        var name = global::System.Text.Encoding.Latin1.GetString(r.Data, Math.Min(18, r.Data.Length),
            Math.Max(0, Math.Min(32, r.Data.Length - 18)));
        var end = name.IndexOf('\0');

        if (end >= 0) name = name[..end];

        return new MetaFont
        {
            Height = height > 0 ? height : 12f,
            Escapement = r.Int16(4),
            Bold = r.Int16(8) >= 700,
            Italic = r.Data.Length > 10 && r.Data[10] != 0,
            Underline = r.Data.Length > 11 && r.Data[11] != 0,
            Strikeout = r.Data.Length > 12 && r.Data[12] != 0,
            Name = name.Length > 0 ? name : "Arial",
        };
    }
}
