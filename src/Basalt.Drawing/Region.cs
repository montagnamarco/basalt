using System.Drawing.Drawing2D;
using SkiaSharp;

namespace System.Drawing;

/// <summary>An area of the plane, built by combining rectangles and paths.</summary>
/// <remarks>
/// Kept as one Skia path in the region's own coordinates; null stands for the
/// infinite region, which is what a new Region is. Combining with the
/// infinite region uses GDI+'s own stand-in for it, a square 8388608 wide
/// centred on the origin, so that its bounds read back as GDI+'s do.
/// </remarks>
public sealed class Region : MarshalByRefObject, IDisposable
{
    internal static readonly RectangleF InfiniteBounds = RectangleF.FromLTRB(-4194304f, -4194304f, 4194304f, 4194304f);

    private SKPath? _area;

    public Region()
    {
    }

    public Region(RectangleF rect) => _area = RectanglePath(rect);

    public Region(Rectangle rect)
        : this((RectangleF)rect)
    {
    }

    public Region(GraphicsPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _area = path.ToSkia();
    }

    public Region(RegionData rgnData)
    {
        ArgumentNullException.ThrowIfNull(rgnData);

        _area = RegionDataFormat.Read(rgnData.Data);
    }

    private Region(SKPath? area) => _area = area;

    ~Region() => Dispose(false);

    /// <summary>The area in device coordinates of whoever asks; null when infinite.</summary>
    internal SKPath? Area => _area;

    /// <summary>A region over a path the caller gives away.</summary>
    internal static Region Adopt(SKPath? area) => new(area);

    public static Region FromHrgn(IntPtr hrgn) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public IntPtr GetHrgn(Graphics g) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public void ReleaseHrgn(IntPtr regionHandle) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public Region Clone() => new(_area is null ? null : new SKPath(_area));

    public void MakeInfinite() => Replace(null);

    public void MakeEmpty() => Replace(new SKPath());

    public void Intersect(RectangleF rect) => Combine(RectanglePath(rect), SKPathOp.Intersect);

    public void Intersect(Rectangle rect) => Intersect((RectangleF)rect);

    public void Intersect(GraphicsPath path) => Combine(PathOf(path), SKPathOp.Intersect);

    public void Intersect(Region region) => Combine(AreaOf(region), SKPathOp.Intersect);

    public void Union(RectangleF rect) => Combine(RectanglePath(rect), SKPathOp.Union);

    public void Union(Rectangle rect) => Union((RectangleF)rect);

    public void Union(GraphicsPath path) => Combine(PathOf(path), SKPathOp.Union);

    public void Union(Region region) => Combine(AreaOf(region), SKPathOp.Union);

    public void Xor(RectangleF rect) => Combine(RectanglePath(rect), SKPathOp.Xor);

    public void Xor(Rectangle rect) => Xor((RectangleF)rect);

    public void Xor(GraphicsPath path) => Combine(PathOf(path), SKPathOp.Xor);

    public void Xor(Region region) => Combine(AreaOf(region), SKPathOp.Xor);

    public void Exclude(RectangleF rect) => Combine(RectanglePath(rect), SKPathOp.Difference);

    public void Exclude(Rectangle rect) => Exclude((RectangleF)rect);

    public void Exclude(GraphicsPath path) => Combine(PathOf(path), SKPathOp.Difference);

    public void Exclude(Region region) => Combine(AreaOf(region), SKPathOp.Difference);

    public void Complement(RectangleF rect) => Combine(RectanglePath(rect), SKPathOp.ReverseDifference);

    public void Complement(Rectangle rect) => Complement((RectangleF)rect);

    public void Complement(GraphicsPath path) => Combine(PathOf(path), SKPathOp.ReverseDifference);

    public void Complement(Region region) => Combine(AreaOf(region), SKPathOp.ReverseDifference);

    public void Translate(int dx, int dy) => Translate((float)dx, dy);

    public void Translate(float dx, float dy) => _area?.Transform(SKMatrix.CreateTranslation(dx, dy));

    public void Transform(Matrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        _area?.Transform(matrix.Value.ToSkia());
    }

    /// <summary>The smallest rectangle around the region.</summary>
    /// <remarks>
    /// In the region's own coordinates. GDI+ takes the Graphics only to know
    /// the device; the answer does not depend on its transform.
    /// </remarks>
    public RectangleF GetBounds(Graphics g)
    {
        ArgumentNullException.ThrowIfNull(g);

        return BoundsOf(_area);
    }

    public bool IsEmpty(Graphics g)
    {
        ArgumentNullException.ThrowIfNull(g);

        return _area is not null && HasNoArea(_area);
    }

    public bool IsInfinite(Graphics g)
    {
        ArgumentNullException.ThrowIfNull(g);

        return _area is null;
    }

    public bool Equals(Region region, Graphics g)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(g);

        if (_area is null || region._area is null) return _area is null && region._area is null;

        using var difference = _area.Op(region._area, SKPathOp.Xor);

        return difference is null || HasNoArea(difference);
    }

    public bool IsVisible(float x, float y) => _area is null || _area.Contains(x, y);

    public bool IsVisible(float x, float y, Graphics? g) => IsVisible(x, y);

    public bool IsVisible(PointF point) => IsVisible(point.X, point.Y);

    public bool IsVisible(PointF point, Graphics? g) => IsVisible(point.X, point.Y);

    public bool IsVisible(Point point) => IsVisible((float)point.X, point.Y);

    public bool IsVisible(Point point, Graphics? g) => IsVisible((float)point.X, point.Y);

    public bool IsVisible(int x, int y, Graphics? g) => IsVisible((float)x, y);

    public bool IsVisible(float x, float y, float width, float height) => IsVisible(new RectangleF(x, y, width, height));

    public bool IsVisible(float x, float y, float width, float height, Graphics? g) => IsVisible(new RectangleF(x, y, width, height));

    public bool IsVisible(int x, int y, int width, int height) => IsVisible(new RectangleF(x, y, width, height));

    public bool IsVisible(int x, int y, int width, int height, Graphics? g) => IsVisible(new RectangleF(x, y, width, height));

    public bool IsVisible(Rectangle rect) => IsVisible((RectangleF)rect);

    public bool IsVisible(Rectangle rect, Graphics? g) => IsVisible((RectangleF)rect);

    public bool IsVisible(RectangleF rect, Graphics? g) => IsVisible(rect);

    /// <summary>Whether any part of the rectangle lies in the region.</summary>
    public bool IsVisible(RectangleF rect)
    {
        using var asked = RectanglePath(rect);
        if (_area is null) return !HasNoArea(asked);

        using var overlap = _area.Op(asked, SKPathOp.Intersect);

        return overlap is not null && !HasNoArea(overlap);
    }

    /// <summary>
    /// The region as horizontal bands of whole-pixel rectangles, after the
    /// matrix: how GDI+ hands a region to code that paints it rectangle by
    /// rectangle.
    /// </summary>
    public RectangleF[] GetRegionScans(Matrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        using var area = _area is null ? RectanglePath(InfiniteBounds) : new SKPath(_area);
        area.Transform(matrix.Value.ToSkia());

        var bounds = area.Bounds;
        using var clip = new SKRegion(SKRectI.Round(bounds).Standardized);
        using var region = new SKRegion();
        region.SetPath(area, clip);

        var scans = new List<RectangleF>();
        using var iterator = region.CreateRectIterator();

        while (iterator.Next(out var rect))
        {
            scans.Add(RectangleF.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
        }

        return [.. scans];
    }

    public RegionData? GetRegionData() => new() { Data = RegionDataFormat.Write(_area) };

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!disposing) return;

        _area?.Dispose();
        _area = null;
    }

    internal static bool HasNoArea(SKPath path) =>
        path.IsEmpty || path.TightBounds.Width <= 0 || path.TightBounds.Height <= 0;

    internal static RectangleF BoundsOf(SKPath? area)
    {
        if (area is null) return InfiniteBounds;
        if (HasNoArea(area)) return RectangleF.Empty;

        var bounds = area.TightBounds;

        return RectangleF.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    internal static SKPath RectanglePath(RectangleF rect)
    {
        var path = new SKPath();

        // A rectangle with no area, or turned inside out, covers nothing.
        if (rect.Width > 0 && rect.Height > 0) path.AddRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom));

        return path;
    }

    private static SKPath PathOf(GraphicsPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.ToSkia();
    }

    private static SKPath? AreaOf(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);

        return region._area is null ? null : new SKPath(region._area);
    }

    /// <summary>Combines with a shape the region then owns; null is the infinite region.</summary>
    private void Combine(SKPath? shape, SKPathOp operation)
    {
        using var given = shape;
        using var plane = RectanglePath(InfiniteBounds);

        var result = (_area, given, operation) switch
        {
            // With the infinite region the answer is known without computing it.
            (null, _, SKPathOp.Intersect) => Copy(given),
            (_, null, SKPathOp.Intersect) => Copy(_area),
            (null, _, SKPathOp.Union) or (_, null, SKPathOp.Union) => null,
            (_, null, SKPathOp.Difference) => new SKPath(),
            (null, _, SKPathOp.ReverseDifference) => new SKPath(),
            _ => (_area ?? plane).Op(given ?? plane, operation) ?? throw new InvalidOperationException("The region could not be combined."),
        };

        Replace(result);
    }

    private static SKPath? Copy(SKPath? path) => path is null ? null : new SKPath(path);

    private void Replace(SKPath? area)
    {
        _area?.Dispose();
        _area = area;
    }
}
