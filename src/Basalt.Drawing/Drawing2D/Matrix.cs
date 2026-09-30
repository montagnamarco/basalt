using System.Numerics;
using System.Runtime.InteropServices;

namespace System.Drawing.Drawing2D;

/// <summary>
/// A 3x2 affine transform in GDI+'s convention: row vectors, elements named
/// m11 m12 m21 m22 dx dy, angles in degrees clockwise (the y axis points
/// down), MatrixOrder.Prepend as the default meaning "apply the new
/// operation first".
/// </summary>
public sealed class Matrix : MarshalByRefObject, IDisposable
{
    private Matrix3x2 _value;

    public Matrix() => _value = Matrix3x2.Identity;

    public Matrix(float m11, float m12, float m21, float m22, float dx, float dy) =>
        _value = new Matrix3x2(m11, m12, m21, m22, dx, dy);

    public Matrix(Matrix3x2 matrix) => _value = matrix;

    public Matrix(Rectangle rect, Point[] plgpts)
        : this(RectangleToParallelogram(rect, RequireThree(plgpts)))
    {
    }

    public Matrix(RectangleF rect, PointF[] plgpts)
        : this(RectangleToParallelogram(rect, RequireThree(plgpts)))
    {
    }

    ~Matrix() => Dispose(false);

    /// <summary>The affine transform as System.Numerics represents it: the same six numbers, same layout.</summary>
    public Matrix3x2 MatrixElements
    {
        get => _value;
        set => _value = value;
    }

    public float[] Elements => [_value.M11, _value.M12, _value.M21, _value.M22, _value.M31, _value.M32];

    public float OffsetX => _value.M31;

    public float OffsetY => _value.M32;

    public bool IsIdentity => _value.IsIdentity;

    public bool IsInvertible => Matrix3x2.Invert(_value, out _);

    internal Matrix3x2 Value => _value;

    public Matrix Clone() => new(_value);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Matrix other) return false;

        // Copied to locals first: MarshalByRefObject warns (CS1690) about
        // reading a struct field straight off another instance, since a
        // remoted proxy could not honour that read.
        var otherValue = other._value;
        return _value.M11 == otherValue.M11 && _value.M12 == otherValue.M12 &&
            _value.M21 == otherValue.M21 && _value.M22 == otherValue.M22 &&
            _value.M31 == otherValue.M31 && _value.M32 == otherValue.M32;
    }

    public override int GetHashCode() => HashCode.Combine(_value.M11, _value.M12, _value.M21, _value.M22, _value.M31, _value.M32);

    public void Reset() => _value = Matrix3x2.Identity;

    /// <summary>Replaces this matrix with the one that undoes it.</summary>
    /// <remarks>
    /// A matrix that flattens the plane has no inverse. GDI+ answers
    /// InvalidParameter, which System.Drawing turns into this ArgumentException;
    /// leaving the matrix as it was would let the caller go on with a transform
    /// that is not the inverse it asked for.
    /// </remarks>
    public void Invert()
    {
        if (!Matrix3x2.Invert(_value, out var inverted)) throw new ArgumentException("Parameter is not valid.");

        _value = inverted;
    }

    public void Multiply(Matrix matrix) => Multiply(matrix, MatrixOrder.Prepend);

    public void Multiply(Matrix matrix, MatrixOrder order)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        _value = MatrixInterop.Combine(_value, matrix._value, order);
    }

    public void Rotate(float angle) => Rotate(angle, MatrixOrder.Prepend);

    public void Rotate(float angle, MatrixOrder order) => _value = MatrixInterop.Combine(_value, RotationOf(angle), order);

    public void RotateAt(float angle, PointF point) => RotateAt(angle, point, MatrixOrder.Prepend);

    public void RotateAt(float angle, PointF point, MatrixOrder order) =>
        _value = MatrixInterop.Combine(_value, Matrix3x2.CreateRotation(ToRadians(angle), new Vector2(point.X, point.Y)), order);

    public void Scale(float scaleX, float scaleY) => Scale(scaleX, scaleY, MatrixOrder.Prepend);

    public void Scale(float scaleX, float scaleY, MatrixOrder order) =>
        _value = MatrixInterop.Combine(_value, Matrix3x2.CreateScale(scaleX, scaleY), order);

    public void Shear(float shearX, float shearY) => Shear(shearX, shearY, MatrixOrder.Prepend);

    public void Shear(float shearX, float shearY, MatrixOrder order) =>
        _value = MatrixInterop.Combine(_value, new Matrix3x2(1, shearY, shearX, 1, 0, 0), order);

    public void Translate(float offsetX, float offsetY) => Translate(offsetX, offsetY, MatrixOrder.Prepend);

    public void Translate(float offsetX, float offsetY, MatrixOrder order) =>
        _value = MatrixInterop.Combine(_value, Matrix3x2.CreateTranslation(offsetX, offsetY), order);

    public void TransformPoints(PointF[] pts)
    {
        ArgumentNullException.ThrowIfNull(pts);

        for (var i = 0; i < pts.Length; i++) pts[i] = TransformPoint(pts[i]);
    }

    public void TransformPoints(Point[] pts)
    {
        ArgumentNullException.ThrowIfNull(pts);

        for (var i = 0; i < pts.Length; i++) pts[i] = RoundToPoint(TransformPoint(pts[i]));
    }

    // GDI+'s span overloads take a ReadOnlySpan even though they mutate the
    // points in place: a ReadOnlySpan<T> and a Span<T> share the exact same
    // memory layout, and this is the trick the framework itself relies on to
    // keep this signature while still writing through it.
    public void TransformPoints(ReadOnlySpan<Point> pts)
    {
        var mutable = AsMutable(pts);
        for (var i = 0; i < mutable.Length; i++) mutable[i] = RoundToPoint(TransformPoint(mutable[i]));
    }

    public void TransformPoints(ReadOnlySpan<PointF> pts)
    {
        var mutable = AsMutable(pts);
        for (var i = 0; i < mutable.Length; i++) mutable[i] = TransformPoint(mutable[i]);
    }

    public void TransformVectors(PointF[] pts)
    {
        ArgumentNullException.ThrowIfNull(pts);

        for (var i = 0; i < pts.Length; i++) pts[i] = TransformVector(pts[i]);
    }

    public void TransformVectors(Point[] pts)
    {
        ArgumentNullException.ThrowIfNull(pts);

        for (var i = 0; i < pts.Length; i++) pts[i] = RoundToPoint(TransformVector(pts[i]));
    }

    public void TransformVectors(ReadOnlySpan<Point> pts)
    {
        var mutable = AsMutable(pts);
        for (var i = 0; i < mutable.Length; i++) mutable[i] = RoundToPoint(TransformVector(mutable[i]));
    }

    public void TransformVectors(ReadOnlySpan<PointF> pts)
    {
        var mutable = AsMutable(pts);
        for (var i = 0; i < mutable.Length; i++) mutable[i] = TransformVector(mutable[i]);
    }

    /// <summary>The legacy, integer-only name for TransformVectors on whole-pixel points.</summary>
    public void VectorTransformPoints(Point[] pts) => TransformVectors(pts);

    public void VectorTransformPoints(ReadOnlySpan<Point> pts) => TransformVectors(pts);

    private PointF TransformPoint(PointF p) =>
        new(p.X * _value.M11 + p.Y * _value.M21 + _value.M31, p.X * _value.M12 + p.Y * _value.M22 + _value.M32);

    private PointF TransformVector(PointF p) =>
        new(p.X * _value.M11 + p.Y * _value.M21, p.X * _value.M12 + p.Y * _value.M22);

    private static Span<T> AsMutable<T>(ReadOnlySpan<T> span) => MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(span), span.Length);

    private static Point RoundToPoint(PointF p) => new((int)MathF.Round(p.X), (int)MathF.Round(p.Y));

    private static Matrix3x2 RotationOf(float angleDegrees) => Matrix3x2.CreateRotation(ToRadians(angleDegrees));

    private static float ToRadians(float degrees) => degrees * MathF.PI / 180f;

    private static T[] RequireThree<T>(T[] plgpts)
    {
        ArgumentNullException.ThrowIfNull(plgpts);
        if (plgpts.Length != 3) throw new ArgumentException("The parallelogram must be described by exactly 3 points.", nameof(plgpts));

        return plgpts;
    }

    /// <summary>
    /// The matrix that sends rect's upper-left, upper-right and lower-left
    /// corners to plgpts[0], plgpts[1] and plgpts[2] respectively: GDI+'s
    /// documented meaning of the (rect, parallelogram) constructor.
    /// </summary>
    private static Matrix3x2 RectangleToParallelogram(RectangleF rect, PointF[] plgpts)
    {
        var (originX, originY) = (plgpts[0].X, plgpts[0].Y);

        // The upper-right corner moves rect.Width along the transform's x
        // basis vector; the lower-left corner moves rect.Height along its y
        // basis vector. Degenerate (zero-sized) rectangles leave that basis
        // vector as the identity's, the least surprising fallback.
        var m11 = rect.Width == 0 ? 1f : (plgpts[1].X - originX) / rect.Width;
        var m12 = rect.Width == 0 ? 0f : (plgpts[1].Y - originY) / rect.Width;
        var m21 = rect.Height == 0 ? 0f : (plgpts[2].X - originX) / rect.Height;
        var m22 = rect.Height == 0 ? 1f : (plgpts[2].Y - originY) / rect.Height;

        var dx = originX - rect.X * m11 - rect.Y * m21;
        var dy = originY - rect.X * m12 - rect.Y * m22;

        return new Matrix3x2(m11, m12, m21, m22, dx, dy);
    }

    private static Matrix3x2 RectangleToParallelogram(Rectangle rect, Point[] plgpts) => RectangleToParallelogram(
        new RectangleF(rect.X, rect.Y, rect.Width, rect.Height),
        [new PointF(plgpts[0].X, plgpts[0].Y), new PointF(plgpts[1].X, plgpts[1].Y), new PointF(plgpts[2].X, plgpts[2].Y)]);
}
