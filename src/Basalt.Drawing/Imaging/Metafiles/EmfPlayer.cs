using SkiaSharp;

namespace System.Drawing.Imaging.Metafiles;

/// <summary>Interprets an enhanced metafile's records (MS-EMF).</summary>
/// <remarks>
/// Ported from Majorsilence.Forms.Drawing.Common
/// (Majorsilence.Forms.Drawing.Common/Metafiles/EmfPlayer.cs), MIT licensed, copyright Jonathan
/// Pobst and Peter Gill (Majorsilence). Adapted to this assembly's namespace and style, and with
/// the bitmap records (BitBlt, StretchBlt, StretchDIBits, AlphaBlend) left unsupported rather than
/// decoding device-independent bitmaps, which this port does not carry over.
/// </remarks>
internal sealed class EmfPlayer(SKCanvas canvas, SKMatrix output, SKSize deviceExtent, List<EmfPlusRecordType> unsupported)
    : MetafilePlayer(canvas, output, deviceExtent, unsupported)
{
    internal void Play(IReadOnlyList<MetafileRecord> records)
    {
        try
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
        finally
        {
            // A truncated metafile can end inside a path it never closed.
            RecordingPath?.Dispose();
            RecordingPath = null;
            CompletedPath?.Dispose();
            CompletedPath = null;
        }
    }

    protected override EmfPlusRecordType ToRecordType(int raw) => (EmfPlusRecordType)raw;

    private void Execute(MetafileRecord r)
    {
        switch (r.Type)
        {
            case Emr.Header:
            case Emr.Eof:
            case Emr.Comment:
                break;

            case Emr.SetWindowOrgEx:
                Dc.WindowOrigin = new SKPoint(r.Int32(0), r.Int32(4));
                Dc.ExtentsSet = true;
                break;
            case Emr.SetWindowExtEx:
                Dc.WindowExtent = new SKSize(r.Int32(0), r.Int32(4));
                Dc.ExtentsSet = true;
                break;
            case Emr.SetViewportOrgEx:
                Dc.ViewportOrigin = new SKPoint(r.Int32(0), r.Int32(4));
                Dc.ExtentsSet = true;
                break;
            case Emr.SetViewportExtEx:
                Dc.ViewportExtent = new SKSize(r.Int32(0), r.Int32(4));
                Dc.ExtentsSet = true;
                break;
            case Emr.SetMapMode:
                Dc.MapMode = r.Int32(0);
                break;

            case Emr.SetWorldTransform:
                Dc.World = ReadTransform(r, 0);
                break;
            case Emr.ModifyWorldTransform:
                {
                    var transform = ReadTransform(r, 0);

                    Dc.World = r.Int32(24) switch
                    {
                        1 => SKMatrix.Identity,
                        2 => transform.PreConcat(Dc.World),
                        _ => Dc.World.PreConcat(transform),
                    };

                    break;
                }

            case Emr.SaveDC:
                SaveDc();
                break;
            case Emr.RestoreDC:
                RestoreDc(r.Int32(0));
                break;
            case Emr.SetTextColor:
                Dc.TextColor = ColorRef(r.UInt32(0));
                break;
            case Emr.SetBkColor:
                Dc.BackColor = ColorRef(r.UInt32(0));
                break;
            case Emr.SetBkMode:
                Dc.BackMode = r.Int32(0);
                break;
            case Emr.SetTextAlign:
                Dc.TextAlign = r.Int32(0);
                break;
            case Emr.SetPolyFillMode:
                Dc.PolyFillMode = r.Int32(0);
                break;

            case Emr.CreatePen:
                StoreObject(r.Int32(0), new MetaPen
                {
                    Style = r.Int32(4),
                    Width = r.Int32(8),
                    Color = ColorRef(r.UInt32(16)),
                });
                break;
            case Emr.ExtCreatePen:
                StoreObject(r.Int32(0), new MetaPen
                {
                    Style = r.Int32(20) & 0xF,
                    Width = r.Int32(24),
                    Color = ColorRef(r.UInt32(32)),
                });
                break;
            case Emr.CreateBrushIndirect:
                StoreObject(r.Int32(0), new MetaBrush
                {
                    Style = r.Int32(4),
                    Color = ColorRef(r.UInt32(8)),
                    Hatch = r.Int32(12),
                });
                break;
            case Emr.ExtCreateFontIndirectW:
                StoreObject(r.Int32(0), ReadFont(r, 4));
                break;
            case Emr.SelectObject:
                SelectObject(r.Int32(0));
                break;
            case Emr.DeleteObject:
                DeleteObject(r.Int32(0));
                break;

            case Emr.MoveToEx:
                Dc.Current = new SKPoint(r.Int32(0), r.Int32(4));
                break;
            case Emr.LineTo:
                {
                    var to = new SKPoint(r.Int32(0), r.Int32(4));
                    using var path = new SKPath();
                    path.MoveTo(Dc.Current);
                    path.LineTo(to);
                    Emit(path, close: false);
                    Dc.Current = to;
                    break;
                }

            case Emr.Polyline:
            case Emr.Polyline16:
                DrawPoly(r, closed: false, continues: false);
                break;
            case Emr.PolylineTo:
            case Emr.PolylineTo16:
                DrawPoly(r, closed: false, continues: true);
                break;
            case Emr.Polygon:
            case Emr.Polygon16:
                DrawPoly(r, closed: true, continues: false);
                break;

            case Emr.PolyBezier:
            case Emr.PolyBezier16:
            case Emr.PolyBezierTo:
            case Emr.PolyBezierTo16:
                {
                    var small = r.Type is Emr.PolyBezier16 or Emr.PolyBezierTo16;
                    var continues = r.Type is Emr.PolyBezierTo or Emr.PolyBezierTo16;
                    var points = ReadPoints(r, r.Int32(16), small, 20);

                    using var path = BuildBezier(points, continues ? Dc.Current : null);
                    Emit(path, close: false);

                    if (points.Length > 0) Dc.Current = points[^1];

                    break;
                }

            case Emr.PolyPolyline:
            case Emr.PolyPolyline16:
                DrawPolyPoly(r, closed: false);
                break;
            case Emr.PolyPolygon:
            case Emr.PolyPolygon16:
                DrawPolyPoly(r, closed: true);
                break;

            case Emr.Rectangle:
                {
                    using var path = new SKPath();
                    path.AddRect(ReadRect(r, 0));
                    Emit(path, close: true);
                    break;
                }
            case Emr.Ellipse:
                {
                    using var path = new SKPath();
                    path.AddOval(ReadRect(r, 0));
                    Emit(path, close: true);
                    break;
                }
            case Emr.RoundRect:
                {
                    using var path = new SKPath();
                    path.AddRoundRect(ReadRect(r, 0), r.Int32(16) / 2f, r.Int32(20) / 2f);
                    Emit(path, close: true);
                    break;
                }
            case Emr.Arc:
            case Emr.Chord:
            case Emr.Pie:
                {
                    var kind = r.Type switch
                    {
                        Emr.Pie => ArcKind.Pie,
                        Emr.Chord => ArcKind.Chord,
                        _ => ArcKind.Arc,
                    };

                    using var path = new SKPath();
                    AddArc(path, ReadRect(r, 0),
                        new SKPoint(r.Int32(16), r.Int32(20)),
                        new SKPoint(r.Int32(24), r.Int32(28)), kind);

                    Emit(path, close: kind != ArcKind.Arc);
                    break;
                }

            case Emr.SetPixelV:
                {
                    ApplyTransform();
                    using var paint = new SKPaint { Color = ColorRef(r.UInt32(8)) };
                    Canvas.DrawPoint(r.Int32(0), r.Int32(4), paint);
                    break;
                }

            case Emr.BeginPath:
                RecordingPath?.Dispose();
                RecordingPath = new SKPath();
                RecordingPath.MoveTo(Dc.Current);
                break;
            case Emr.EndPath:
                CompletedPath?.Dispose();
                CompletedPath = RecordingPath;
                RecordingPath = null;
                break;
            case Emr.CloseFigure:
                RecordingPath?.Close();
                break;
            case Emr.AbortPath:
                RecordingPath?.Dispose();
                RecordingPath = null;
                break;
            case Emr.FillPath:
            case Emr.StrokePath:
            case Emr.StrokeAndFillPath:
                if (CompletedPath is { } finished)
                {
                    if (r.Type == Emr.StrokePath) StrokeOnly(finished);
                    else FillThenStroke(finished);
                }

                break;
            case Emr.SelectClipPath:
                if (CompletedPath is { } clip)
                {
                    ApplyTransform();
                    Canvas.ClipPath(clip);
                }

                break;

            case Emr.IntersectClipRect:
                ApplyTransform();
                Canvas.ClipRect(ReadRect(r, 0));
                break;

            case Emr.ExtTextOutA:
            case Emr.ExtTextOutW:
                {
                    const int textAt = 28;

                    var x = r.Int32(textAt);
                    var y = r.Int32(textAt + 4);
                    var count = r.Int32(textAt + 8);
                    var offset = r.Int32(textAt + 12) - 8;

                    if (count <= 0 || count > 0x10000 || offset < 0) break;

                    var text = r.Type == Emr.ExtTextOutW
                        ? ReadName(r.Data, offset, count)
                        : ReadAnsi(r.Data, offset, count);

                    DrawText(x, y, text);
                    break;
                }

            // Bitmap records: no device-independent bitmap decoder in this port, so the picture
            // stays correct for its vector content while the blit itself is reported as skipped.
            case Emr.StretchDIBits:
            case Emr.BitBlt:
            case Emr.StretchBlt:
            case Emr.AlphaBlend:
                Skip(r.Type);
                break;

            default:
                Skip(r.Type);
                break;
        }
    }

    // Emits a path as either an outline or a filled shape, honouring an open BeginPath: while a
    // path is being recorded, drawing records contribute to it instead of painting.
    private void Emit(SKPath path, bool close)
    {
        if (RecordingPath is { } recording)
        {
            recording.AddPath(path);
            return;
        }

        if (close) FillThenStroke(path);
        else StrokeOnly(path);
    }

    private void DrawPoly(MetafileRecord r, bool closed, bool continues)
    {
        var small = r.Type is Emr.Polyline16 or Emr.Polygon16 or Emr.PolylineTo16;
        var count = r.Int32(16);
        var points = ReadPoints(r, count, small, 20);

        if (points.Length == 0) return;

        using var path = new SKPath();

        if (continues)
        {
            path.MoveTo(Dc.Current);

            foreach (var point in points) path.LineTo(point);
        }
        else
        {
            path.MoveTo(points[0]);

            for (var i = 1; i < points.Length; i++) path.LineTo(points[i]);

            if (closed) path.Close();
        }

        Emit(path, closed);
        Dc.Current = points[^1];
    }

    private void DrawPolyPoly(MetafileRecord r, bool closed)
    {
        var small = r.Type is Emr.PolyPolyline16 or Emr.PolyPolygon16;
        var polygons = r.Int32(16);
        var total = r.Int32(20);

        if (polygons <= 0 || polygons > 0x10000 || total <= 0 || total > 0x100000) return;

        var counts = new int[polygons];

        for (var i = 0; i < polygons; i++) counts[i] = r.Int32(24 + (i * 4));

        var points = ReadPoints(r, total, small, 24 + (polygons * 4));
        using var path = new SKPath();
        var index = 0;

        foreach (var count in counts)
        {
            if (count <= 0 || index + count > points.Length) break;

            path.MoveTo(points[index]);

            for (var i = 1; i < count; i++) path.LineTo(points[index + i]);

            if (closed) path.Close();

            index += count;
        }

        Emit(path, closed);
    }

    private static SKPoint[] ReadPoints(MetafileRecord r, int count, bool small, int offset)
    {
        if (count <= 0 || count > 0x100000) return [];

        var size = small ? 4 : 8;
        var available = (r.Data.Length - offset) / size;
        var points = new SKPoint[Math.Min(count, Math.Max(available, 0))];

        for (var i = 0; i < points.Length; i++)
        {
            var at = offset + (i * size);

            points[i] = small
                ? new SKPoint(r.Int16(at), r.Int16(at + 2))
                : new SKPoint(r.Int32(at), r.Int32(at + 4));
        }

        return points;
    }

    private static SKRect ReadRect(MetafileRecord r, int offset) =>
        SKRect.Create(r.Int32(offset), r.Int32(offset + 4),
            r.Int32(offset + 8) - r.Int32(offset),
            r.Int32(offset + 12) - r.Int32(offset + 4)).Standardized;

    private static SKMatrix ReadTransform(MetafileRecord r, int offset) => new()
    {
        ScaleX = r.Single(offset),
        SkewY = r.Single(offset + 4),
        SkewX = r.Single(offset + 8),
        ScaleY = r.Single(offset + 12),
        TransX = r.Single(offset + 16),
        TransY = r.Single(offset + 20),
        Persp2 = 1f,
    };

    private static MetaFont ReadFont(MetafileRecord r, int offset)
    {
        var height = r.Int32(offset);

        return new MetaFont
        {
            Height = Math.Abs(height) is var h && h > 0 ? h : 12f,
            Escapement = r.Int32(offset + 8),
            Bold = r.Int32(offset + 16) >= 700,
            Italic = offset + 20 < r.Data.Length && r.Data[offset + 20] != 0,
            Underline = offset + 21 < r.Data.Length && r.Data[offset + 21] != 0,
            Strikeout = offset + 22 < r.Data.Length && r.Data[offset + 22] != 0,
            Name = ReadName(r.Data, offset + 28, 32) is { Length: > 0 } name ? name : "Arial",
        };
    }

    private static string ReadAnsi(byte[] data, int offset, int count)
    {
        if (offset < 0 || offset >= data.Length) return string.Empty;

        var length = Math.Min(count, data.Length - offset);
        var end = Array.IndexOf(data, (byte)0, offset, length);

        if (end >= 0) length = end - offset;

        return length <= 0 ? string.Empty : global::System.Text.Encoding.Latin1.GetString(data, offset, length);
    }
}
