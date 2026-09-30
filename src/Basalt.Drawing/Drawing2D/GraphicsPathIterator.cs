namespace System.Drawing.Drawing2D;

/// <summary>
/// Walks a path figure by figure, section by section between markers, and
/// run by run of the same kind of segment.
/// </summary>
/// <remarks>
/// A snapshot of the path taken when the iterator is made, as in GDI+:
/// changing the path afterwards does not change what is walked. The index
/// ranges are inclusive, as GDI+ returns them.
/// </remarks>
public sealed class GraphicsPathIterator : MarshalByRefObject, IDisposable
{
    private readonly PointF[] _points;
    private readonly byte[] _types;

    private int _nextSubpath;
    private int _nextMarker;

    // The figure NextSubpath last returned, which NextPathType walks.
    private int _subpathFirst;
    private int _subpathLast = -1;
    private int _nextTypeRun;

    public GraphicsPathIterator(GraphicsPath? path)
    {
        _points = path?.PathPoints ?? [];
        _types = path?.PathTypes ?? [];
    }

    ~GraphicsPathIterator() => Dispose(false);

    public int Count => _points.Length;

    public int SubpathCount
    {
        get
        {
            var count = 0;

            for (var i = 0; i < _types.Length; i++)
            {
                if ((_types[i] & PathGeometry.TypeMask) == PathGeometry.Start) count++;
            }

            return count;
        }
    }

    public bool HasCurve()
    {
        foreach (var type in _types)
        {
            if ((type & PathGeometry.TypeMask) == PathGeometry.Bezier) return true;
        }

        return false;
    }

    public void Rewind()
    {
        _nextSubpath = 0;
        _nextMarker = 0;
        _subpathFirst = 0;
        _subpathLast = -1;
        _nextTypeRun = 0;
    }

    /// <summary>The next figure: its first and last index and whether it is closed; 0 at the end.</summary>
    public int NextSubpath(out int startIndex, out int endIndex, out bool isClosed)
    {
        startIndex = 0;
        endIndex = 0;
        isClosed = false;

        if (_nextSubpath >= _points.Length) return 0;

        var first = _nextSubpath;
        var last = first;

        while (last + 1 < _types.Length && (_types[last + 1] & PathGeometry.TypeMask) != PathGeometry.Start)
        {
            last++;
        }

        startIndex = first;
        endIndex = last;
        isClosed = (_types[last] & PathGeometry.Close) != 0;

        _nextSubpath = last + 1;
        _subpathFirst = first;
        _subpathLast = last;
        _nextTypeRun = first;

        return last - first + 1;
    }

    /// <summary>The next figure, copied into <paramref name="path"/>.</summary>
    public int NextSubpath(GraphicsPath path, out bool isClosed)
    {
        ArgumentNullException.ThrowIfNull(path);

        var count = NextSubpath(out var first, out var last, out isClosed);
        path.Reset();

        if (count > 0)
        {
            using var figure = new GraphicsPath(_points.AsSpan(first, count), _types.AsSpan(first, count));
            path.AddPath(figure, connect: false);
        }

        return count;
    }

    /// <summary>
    /// The next run of segments of one kind inside the current figure: all
    /// lines, or all Bézier curves. A run starts at the point its first segment
    /// leaves from, so consecutive runs share a point.
    /// </summary>
    public int NextPathType(out byte pathType, out int startIndex, out int endIndex)
    {
        pathType = 0;
        startIndex = 0;
        endIndex = 0;

        if (_subpathLast < 0 || _nextTypeRun >= _subpathLast) return 0;

        var first = _nextTypeRun;
        var kind = (byte)(_types[first + 1] & PathGeometry.TypeMask);
        var last = first + 1;

        while (last + 1 <= _subpathLast && (_types[last + 1] & PathGeometry.TypeMask) == kind)
        {
            last++;
        }

        pathType = kind;
        startIndex = first;
        endIndex = last;
        _nextTypeRun = last;

        return last - first + 1;
    }

    /// <summary>The next section ending at a marker, or at the end of the path; 0 at the end.</summary>
    public int NextMarker(out int startIndex, out int endIndex)
    {
        startIndex = 0;
        endIndex = 0;

        if (_nextMarker >= _points.Length) return 0;

        var first = _nextMarker;
        var last = first;

        while (last < _types.Length - 1 && (_types[last] & PathGeometry.Marker) == 0)
        {
            last++;
        }

        startIndex = first;
        endIndex = last;
        _nextMarker = last + 1;

        return last - first + 1;
    }

    public int NextMarker(GraphicsPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var count = NextMarker(out var first, out _);
        path.Reset();

        if (count > 0)
        {
            var types = _types.AsSpan(first, count).ToArray();

            // A section cut from the middle of a figure opens a figure of its own.
            types[0] = (byte)((types[0] & ~PathGeometry.TypeMask) | PathGeometry.Start);

            using var section = new GraphicsPath(_points.AsSpan(first, count), types);
            path.AddPath(section, connect: false);
        }

        return count;
    }

    public int Enumerate(ref PointF[] points, ref byte[] types) => Enumerate(points.AsSpan(), types.AsSpan());

    /// <summary>Copies as many points and types as both destinations hold.</summary>
    public int Enumerate(Span<PointF> points, Span<byte> types)
    {
        if (points.Length != types.Length) throw new ArgumentException("Parameter is not valid.");

        var count = Math.Min(points.Length, _points.Length);
        _points.AsSpan(0, count).CopyTo(points);
        _types.AsSpan(0, count).CopyTo(types);

        return count;
    }

    public int CopyData(ref PointF[] points, ref byte[] types, int startIndex, int endIndex) =>
        CopyData(points.AsSpan(), types.AsSpan(), startIndex, endIndex);

    /// <summary>Copies the points and types from startIndex to endIndex, both included.</summary>
    public int CopyData(Span<PointF> points, Span<byte> types, int startIndex, int endIndex)
    {
        if (points.Length != types.Length) throw new ArgumentException("Parameter is not valid.");
        if (startIndex < 0 || endIndex >= _points.Length || startIndex > endIndex) return 0;

        var count = Math.Min(endIndex - startIndex + 1, points.Length);
        _points.AsSpan(startIndex, count).CopyTo(points);
        _types.AsSpan(startIndex, count).CopyTo(types);

        return count;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
    }
}
