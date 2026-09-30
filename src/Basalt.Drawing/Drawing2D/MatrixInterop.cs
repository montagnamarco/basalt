using System.Numerics;
using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>
/// Converts between the three affine representations this assembly juggles:
/// GDI+'s row-vector Matrix (m11 m12 m21 m22 dx dy), System.Numerics.Matrix3x2
/// (the same row-vector convention, so the six numbers line up one to one) and
/// Skia's column-vector SKMatrix, which stores the same transform with the
/// rows and columns swapped.
/// </summary>
internal static class MatrixInterop
{
    // The one DPI this assembly reports: Graphics.DpiX/DpiY are always 96,
    // there is no real device to ask, and every GraphicsUnit conversion below
    // is defined relative to it.
    internal const float Dpi = 96f;

    /// <summary>
    /// Skia transforms a column vector (point' = M * point); GDI+ and
    /// System.Numerics.Matrix3x2 transform a row vector (point' = point * M).
    /// The two layouts hold the same six numbers on the diagonal and swapped
    /// off it: measured by transforming (1, 0) and (0, 1) through both and
    /// comparing.
    /// </summary>
    public static SKMatrix ToSkia(this Matrix3x2 m) => new(
        scaleX: m.M11, skewX: m.M21, transX: m.M31,
        skewY: m.M12, scaleY: m.M22, transY: m.M32,
        persp0: 0, persp1: 0, persp2: 1);

    public static Matrix3x2 ToMatrix3x2(this SKMatrix m) => new(
        m11: m.ScaleX, m12: m.SkewY,
        m21: m.SkewX, m22: m.ScaleY,
        m31: m.TransX, m32: m.TransY);

    /// <summary>
    /// Combines a new operation with the current matrix in the order GDI+
    /// names: Prepend (the default) applies the new operation to the
    /// coordinates first, so the combined matrix is operation * current;
    /// Append applies it last, current * operation. System.Numerics.Matrix3x2
    /// multiplication is already "apply the left operand, then the right one"
    /// for row vectors, so this is a direct translation of the two names.
    /// </summary>
    public static Matrix3x2 Combine(Matrix3x2 current, Matrix3x2 operation, MatrixOrder order) =>
        order == MatrixOrder.Append ? Matrix3x2.Multiply(current, operation) : Matrix3x2.Multiply(operation, current);

    /// <summary>
    /// Pixels per unit of measurement, at the fixed 96 DPI this assembly
    /// reports. World, Display and Pixel are already pixels.
    /// </summary>
    public static float UnitScale(GraphicsUnit unit) => unit switch
    {
        GraphicsUnit.Point => Dpi / 72f,
        GraphicsUnit.Inch => Dpi,
        GraphicsUnit.Millimeter => Dpi / 25.4f,
        GraphicsUnit.Document => Dpi / 300f,
        _ => 1f,
    };
}
