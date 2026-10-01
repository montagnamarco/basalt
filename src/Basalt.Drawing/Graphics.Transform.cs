using System.Drawing.Drawing2D;
using System.Numerics;
using System.Runtime.InteropServices;

namespace System.Drawing;

/// <summary>The world transform: what Graphics.Transform, *Transform methods and TransformPoints operate on.</summary>
public sealed partial class Graphics
{
    // Relative to _baseMatrix, never touching it: a Graphics over a control
    // starts already translated to where the control sits, and that baseline
    // must survive ResetTransform and every other transform call.
    private Matrix3x2 _worldTransform = Matrix3x2.Identity;

    public Drawing2D.Matrix Transform
    {
        get => new(_worldTransform);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _worldTransform = value.Value;
        }
    }

    public Matrix3x2 TransformElements
    {
        get => _worldTransform;
        set => _worldTransform = value;
    }

    public void ResetTransform() => _worldTransform = Matrix3x2.Identity;

    public void TranslateTransform(float dx, float dy) => TranslateTransform(dx, dy, MatrixOrder.Prepend);

    public void TranslateTransform(float dx, float dy, MatrixOrder order) =>
        _worldTransform = MatrixInterop.Combine(_worldTransform, Matrix3x2.CreateTranslation(dx, dy), order);

    public void ScaleTransform(float sx, float sy) => ScaleTransform(sx, sy, MatrixOrder.Prepend);

    public void ScaleTransform(float sx, float sy, MatrixOrder order) =>
        _worldTransform = MatrixInterop.Combine(_worldTransform, Matrix3x2.CreateScale(sx, sy), order);

    public void RotateTransform(float angle) => RotateTransform(angle, MatrixOrder.Prepend);

    public void RotateTransform(float angle, MatrixOrder order) =>
        _worldTransform = MatrixInterop.Combine(_worldTransform, Matrix3x2.CreateRotation(angle * MathF.PI / 180f), order);

    public void MultiplyTransform(Drawing2D.Matrix matrix) => MultiplyTransform(matrix, MatrixOrder.Prepend);

    public void MultiplyTransform(Drawing2D.Matrix matrix, MatrixOrder order)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        _worldTransform = MatrixInterop.Combine(_worldTransform, matrix.Value, order);
    }

    public void TransformPoints(CoordinateSpace destSpace, CoordinateSpace srcSpace, PointF[] pts)
    {
        ArgumentNullException.ThrowIfNull(pts);

        var m = SpaceToSpace(srcSpace, destSpace);
        for (var i = 0; i < pts.Length; i++) pts[i] = Apply(m, pts[i]);
    }

    public void TransformPoints(CoordinateSpace destSpace, CoordinateSpace srcSpace, Point[] pts)
    {
        ArgumentNullException.ThrowIfNull(pts);

        var m = SpaceToSpace(srcSpace, destSpace);
        for (var i = 0; i < pts.Length; i++)
        {
            var p = Apply(m, pts[i]);
            pts[i] = new Point((int)MathF.Round(p.X), (int)MathF.Round(p.Y));
        }
    }

    public void TransformPoints(CoordinateSpace destSpace, CoordinateSpace srcSpace, ReadOnlySpan<Point> pts)
    {
        var m = SpaceToSpace(srcSpace, destSpace);
        var mutable = AsMutable(pts);
        for (var i = 0; i < mutable.Length; i++)
        {
            var p = Apply(m, mutable[i]);
            mutable[i] = new Point((int)MathF.Round(p.X), (int)MathF.Round(p.Y));
        }
    }

    public void TransformPoints(CoordinateSpace destSpace, CoordinateSpace srcSpace, ReadOnlySpan<PointF> pts)
    {
        var m = SpaceToSpace(srcSpace, destSpace);
        var mutable = AsMutable(pts);
        for (var i = 0; i < mutable.Length; i++) mutable[i] = Apply(m, mutable[i]);
    }

    /// <summary>
    /// The matrix from World to Device: World goes through the world
    /// transform and then the page unit/scale conversion; Page only goes
    /// through the page conversion; Device is already there. The baseline
    /// matrix of a control-backed Graphics plays no part: GDI+ defines these
    /// three spaces relative to the Graphics object's own target, not the
    /// window it happens to sit in.
    /// </summary>
    private Matrix3x2 ToDeviceSpace(CoordinateSpace space)
    {
        var pageToDevice = Matrix3x2.CreateScale(PixelsPerUnit(_pageUnit) * _pageScale);

        return space switch
        {
            CoordinateSpace.World => MatrixInterop.Combine(_worldTransform, pageToDevice, MatrixOrder.Append),
            CoordinateSpace.Page => pageToDevice,
            _ => Matrix3x2.Identity,
        };
    }

    private Matrix3x2 SpaceToSpace(CoordinateSpace from, CoordinateSpace to)
    {
        var toDeviceFromSrc = ToDeviceSpace(from);
        var toDeviceFromDst = ToDeviceSpace(to);

        // point_dst = point_src * toDeviceFromSrc * invert(toDeviceFromDst).
        return Matrix3x2.Invert(toDeviceFromDst, out var deviceToDst)
            ? MatrixInterop.Combine(toDeviceFromSrc, deviceToDst, MatrixOrder.Append)
            : Matrix3x2.Identity;
    }

    private static PointF Apply(Matrix3x2 m, PointF p) => new(p.X * m.M11 + p.Y * m.M21 + m.M31, p.X * m.M12 + p.Y * m.M22 + m.M32);

    // As in Drawing2D.Matrix: a ReadOnlySpan<T> and a Span<T> share the exact
    // same memory layout, which is how GDI+'s own mutating span overloads get
    // away with taking a ReadOnlySpan parameter.
    private static Span<T> AsMutable<T>(ReadOnlySpan<T> span) => MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(span), span.Length);
}
