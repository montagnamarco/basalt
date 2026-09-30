using System.Drawing.Drawing2D;
using System.Numerics;
using SkiaSharp;

namespace System.Drawing;

/// <summary>The clip: the part of the surface drawing is allowed to reach.</summary>
/// <remarks>
/// Kept as a path in device coordinates, not as a rectangle in the caller's.
/// GDI+ fixes a clip to the surface at the moment it is set: a transform
/// applied afterwards moves what is drawn and leaves the clip where it was,
/// and a clip set under a rotation is not a rectangle at all. A path also
/// gives the combine modes their real meaning: ExcludeClip cuts a hole, which
/// is how a control keeps its children from being painted over.
/// </remarks>
public sealed partial class Graphics
{
    // What GDI+ reports as the bounds of a clip that was never set. The value
    // is the one libgdiplus used and has not been compared with Windows.
    private static readonly RectangleF InfiniteClip = RectangleF.FromLTRB(-4194304f, -4194304f, 4194304f, 4194304f);

    // Null when nothing is clipped.
    private SKPath? _clip;

    public RectangleF ClipBounds => WorldBounds(_clip);

    public bool IsClipEmpty => _clip is not null && HasNoArea(_clip);

    public RectangleF VisibleClipBounds
    {
        get
        {
            using var visible = VisibleClip();

            return WorldBounds(visible);
        }
    }

    public bool IsVisibleClipEmpty
    {
        get
        {
            using var visible = VisibleClip();

            return HasNoArea(visible);
        }
    }

    public void ResetClip() => ReplaceClip(null);

    public void SetClip(Rectangle rect) => SetClip(rect, CombineMode.Replace);

    public void SetClip(RectangleF rect) => SetClip(rect, CombineMode.Replace);

    public void SetClip(Rectangle rect, CombineMode combineMode) => SetClip((RectangleF)rect, combineMode);

    public void SetClip(RectangleF rect, CombineMode combineMode) => CombineClip(DevicePath(rect), combineMode);

    public void SetClip(Graphics g) => SetClip(g, CombineMode.Replace);

    public void SetClip(Graphics g, CombineMode combineMode)
    {
        ArgumentNullException.ThrowIfNull(g);

        CombineClip(Copy(g._clip), combineMode);
    }

    public void IntersectClip(Rectangle rect) => SetClip(rect, CombineMode.Intersect);

    public void IntersectClip(RectangleF rect) => SetClip(rect, CombineMode.Intersect);

    public void ExcludeClip(Rectangle rect) => SetClip(rect, CombineMode.Exclude);

    public void TranslateClip(int dx, int dy) => TranslateClip((float)dx, dy);

    public void TranslateClip(float dx, float dy)
    {
        if (_clip is null) return;

        // The distance is in the caller's units, the clip in device pixels.
        var toDevice = WorldToDevice();
        var deviceX = dx * toDevice.M11 + dy * toDevice.M21;
        var deviceY = dx * toDevice.M12 + dy * toDevice.M22;

        _clip.Transform(SKMatrix.CreateTranslation(deviceX, deviceY));
    }

    public bool IsVisible(Point point) => IsVisible((float)point.X, point.Y);

    public bool IsVisible(PointF point) => IsVisible(point.X, point.Y);

    public bool IsVisible(int x, int y) => IsVisible((float)x, y);

    public bool IsVisible(float x, float y)
    {
        var device = Vector2.Transform(new Vector2(x, y), WorldToDevice());
        using var visible = VisibleClip();

        return visible.Contains(device.X, device.Y);
    }

    public bool IsVisible(Rectangle rect) => IsVisible((RectangleF)rect);

    public bool IsVisible(RectangleF rect)
    {
        using var visible = VisibleClip();
        using var asked = DevicePath(rect);
        using var overlap = Combine(visible, asked, SKPathOp.Intersect);

        return !HasNoArea(overlap);
    }

    public bool IsVisible(int x, int y, int width, int height) => IsVisible(new RectangleF(x, y, width, height));

    public bool IsVisible(float x, float y, float width, float height) => IsVisible(new RectangleF(x, y, width, height));

    public void Flush() => Flush(FlushIntention.Flush);

    public void Flush(FlushIntention intention) => _canvas.Flush();

    /// <summary>A copy of the clip for a saved state to keep.</summary>
    private SKPath? CopyClip() => Copy(_clip);

    private void ReplaceClip(SKPath? clip)
    {
        _clip?.Dispose();
        _clip = clip;
    }

    /// <summary>Combines the clip with a shape; a null shape is the whole plane.</summary>
    private void CombineClip(SKPath? shape, CombineMode mode)
    {
        using var given = shape;

        ReplaceClip(Combined(_clip, given, mode));
    }

    /// <summary>
    /// The clip after the combination, always a path of its own: neither
    /// argument is kept, so the caller is free to dispose both.
    /// </summary>
    private static SKPath? Combined(SKPath? clip, SKPath? shape, CombineMode mode)
    {
        switch (mode)
        {
            case CombineMode.Replace:
                return Copy(shape);

            case CombineMode.Intersect:
                if (clip is null) return Copy(shape);
                if (shape is null) return Copy(clip);

                return Combine(clip, shape, SKPathOp.Intersect);

            case CombineMode.Union:
                return clip is null || shape is null ? null : Combine(clip, shape, SKPathOp.Union);

            case CombineMode.Xor:
                {
                    using var plane = WholePlane();

                    return Combine(clip ?? plane, shape ?? plane, SKPathOp.Xor);
                }

            case CombineMode.Exclude:
                {
                    if (shape is null) return new SKPath();

                    using var plane = WholePlane();

                    return Combine(clip ?? plane, shape, SKPathOp.Difference);
                }

            case CombineMode.Complement:
                {
                    // What of the shape the clip did not already cover.
                    if (clip is null) return new SKPath();

                    using var plane = WholePlane();

                    return Combine(shape ?? plane, clip, SKPathOp.Difference);
                }

            default:
                throw new ArgumentException("Parameter is not valid.", nameof(mode));
        }
    }

    private static SKPath? Copy(SKPath? path) => path is null ? null : new SKPath(path);

    private static SKPath Combine(SKPath first, SKPath second, SKPathOp operation) =>
        first.Op(second, operation) ?? throw new InvalidOperationException("The clip could not be combined.");

    private static SKPath WholePlane()
    {
        var path = new SKPath();
        path.AddRect(new SKRect(InfiniteClip.Left, InfiniteClip.Top, InfiniteClip.Right, InfiniteClip.Bottom));
        return path;
    }

    /// <summary>
    /// A path with no verbs, or one that encloses nothing: a rectangle of no
    /// width is an empty clip to GDI+.
    /// </summary>
    private static bool HasNoArea(SKPath path) => path.IsEmpty || path.TightBounds.Width <= 0 || path.TightBounds.Height <= 0;

    /// <summary>The caller's rectangle as it lies on the surface, which under a rotation is not a rectangle.</summary>
    private SKPath DevicePath(RectangleF rect)
    {
        var path = new SKPath();

        // A rectangle of negative size is nothing, not its mirror image.
        if (rect.Width > 0 && rect.Height > 0)
        {
            path.AddRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom));
            path.Transform(WorldToDevice().ToSkia());
        }

        return path;
    }

    /// <summary>The clip cut down to the surface there is to draw on.</summary>
    private SKPath VisibleClip()
    {
        var surface = new SKPath();
        surface.AddRect(_canvas.DeviceClipBounds);

        if (_clip is null) return surface;

        using (surface)
        {
            return Combine(_clip, surface, SKPathOp.Intersect);
        }
    }

    /// <summary>The bounds of a device path in the caller's coordinates.</summary>
    private RectangleF WorldBounds(SKPath? path)
    {
        if (path is null) return InfiniteClip;
        if (HasNoArea(path)) return RectangleF.Empty;
        if (!Matrix3x2.Invert(WorldToDevice(), out var toWorld)) return InfiniteClip;

        using var inWorld = new SKPath(path);
        inWorld.Transform(toWorld.ToSkia());
        var bounds = inWorld.TightBounds;

        return RectangleF.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }
}
