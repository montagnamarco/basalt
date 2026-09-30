using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace Basalt.Drawing.Tests;

/// <summary>GDI+'s affine transform: row vectors, clockwise degrees, MatrixOrder.Prepend as the default.</summary>
public class MatrixTests
{
    [Fact]
    public void A_new_matrix_is_the_identity()
    {
        var matrix = new Matrix();

        Assert.True(matrix.IsIdentity);
        Assert.Equal(new float[] { 1, 0, 0, 1, 0, 0 }, matrix.Elements);
    }

    [Fact]
    public void The_six_constructor_elements_land_in_the_matching_property()
    {
        var matrix = new Matrix(1, 2, 3, 4, 5, 6);

        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, matrix.Elements);
        Assert.Equal(5, matrix.OffsetX);
        Assert.Equal(6, matrix.OffsetY);
    }

    /// <summary>
    /// Rotating (1, 0) by 90 degrees must land on (0, 1): GDI+ measures angles
    /// clockwise, and its y axis points down, so "clockwise" turns east into
    /// south rather than north.
    /// </summary>
    [Fact]
    public void A_positive_rotation_turns_east_into_south()
    {
        var matrix = new Matrix();
        matrix.Rotate(90);

        var points = new[] { new PointF(1, 0) };
        matrix.TransformPoints(points);

        Assert.Equal(0, points[0].X, 3);
        Assert.Equal(1, points[0].Y, 3);
    }

    /// <summary>
    /// MatrixOrder.Prepend (the default) applies the new operation to the
    /// coordinates first; MatrixOrder.Append applies it last. Combining a
    /// translate and a scale in the two orders must give different results,
    /// or the two names mean nothing.
    /// </summary>
    [Fact]
    public void Prepend_applies_the_new_operation_before_the_existing_matrix_and_append_after_it()
    {
        var prepend = new Matrix();
        prepend.Scale(2, 2);
        prepend.Translate(10, 0, MatrixOrder.Prepend);

        var append = new Matrix();
        append.Scale(2, 2);
        append.Translate(10, 0, MatrixOrder.Append);

        var prependPoints = new[] { new PointF(1, 0) };
        prepend.TransformPoints(prependPoints);

        var appendPoints = new[] { new PointF(1, 0) };
        append.TransformPoints(appendPoints);

        // Prepend: translate first ((1,0) -> (11,0)), then the existing scale ((11,0) -> (22,0)).
        Assert.Equal(new PointF(22, 0), prependPoints[0]);

        // Append: the existing scale first ((1,0) -> (2,0)), then translate ((2,0) -> (12,0)).
        Assert.Equal(new PointF(12, 0), appendPoints[0]);
    }

    [Fact]
    public void Multiply_without_an_order_defaults_to_prepend()
    {
        var withOrder = new Matrix();
        withOrder.Scale(2, 2);
        var translate = new Matrix();
        translate.Translate(10, 0);
        withOrder.Multiply(translate, MatrixOrder.Prepend);

        var withoutOrder = new Matrix();
        withoutOrder.Scale(2, 2);
        withoutOrder.Multiply(translate);

        Assert.Equal(withOrder.Elements, withoutOrder.Elements);
    }

    [Fact]
    public void Translate_moves_a_point_by_exactly_its_offset()
    {
        var matrix = new Matrix();
        matrix.Translate(3, 4);

        var points = new[] { new PointF(10, 10) };
        matrix.TransformPoints(points);

        Assert.Equal(new PointF(13, 14), points[0]);
    }

    [Fact]
    public void TransformVectors_ignores_the_translation_part()
    {
        var matrix = new Matrix();
        matrix.Translate(100, 100);
        matrix.Scale(2, 2, MatrixOrder.Append);

        var vectors = new[] { new PointF(3, 4) };
        matrix.TransformVectors(vectors);

        // Only the scale applies to a vector: no translation offset.
        Assert.Equal(new PointF(6, 8), vectors[0]);
    }

    [Fact]
    public void An_invertible_matrix_round_trips_a_point_through_invert()
    {
        var matrix = new Matrix();
        matrix.Scale(2, 4);
        matrix.Translate(5, 6, MatrixOrder.Append);

        Assert.True(matrix.IsInvertible);

        var original = new PointF(7, 9);
        var points = new[] { original };
        matrix.TransformPoints(points);
        matrix.Invert();
        matrix.TransformPoints(points);

        Assert.Equal(original.X, points[0].X, 3);
        Assert.Equal(original.Y, points[0].Y, 3);
    }

    [Fact]
    public void A_matrix_collapsed_flat_is_not_invertible()
    {
        var matrix = new Matrix();
        matrix.Scale(0, 1);

        Assert.False(matrix.IsInvertible);
    }

    [Fact]
    public void Reset_returns_a_matrix_to_the_identity()
    {
        var matrix = new Matrix();
        matrix.Translate(5, 5);
        matrix.Reset();

        Assert.True(matrix.IsIdentity);
    }

    [Fact]
    public void Clone_is_independent_of_the_original()
    {
        var matrix = new Matrix();
        matrix.Translate(1, 1);

        var clone = matrix.Clone();
        clone.Translate(9, 9);

        Assert.NotEqual(matrix.Elements, clone.Elements);
    }

    [Fact]
    public void Equals_compares_the_six_elements()
    {
        var a = new Matrix(1, 2, 3, 4, 5, 6);
        var b = new Matrix(1, 2, 3, 4, 5, 6);
        var c = new Matrix(1, 2, 3, 4, 5, 7);

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(c));
    }

    [Fact]
    public void MatrixElements_round_trips_through_System_Numerics()
    {
        var numerics = new Matrix3x2(1, 2, 3, 4, 5, 6);
        var matrix = new Matrix { MatrixElements = numerics };

        Assert.Equal(numerics, matrix.MatrixElements);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, matrix.Elements);
    }

    [Fact]
    public void The_rect_and_parallelogram_constructor_maps_the_rectangles_three_named_corners()
    {
        var rect = new RectangleF(0, 0, 10, 20);
        var plgpts = new[] { new PointF(100, 100), new PointF(120, 100), new PointF(100, 140) };
        var matrix = new Matrix(rect, plgpts);

        var corners = new[] { new PointF(rect.X, rect.Y), new PointF(rect.Right, rect.Y), new PointF(rect.X, rect.Bottom) };
        matrix.TransformPoints(corners);

        Assert.Equal(plgpts[0], corners[0]);
        Assert.Equal(plgpts[1], corners[1]);
        Assert.Equal(plgpts[2], corners[2]);
    }

    [Fact]
    public void A_parallelogram_of_the_wrong_length_is_rejected()
    {
        var rect = new RectangleF(0, 0, 1, 1);
        Assert.Throws<ArgumentException>(() => new Matrix(rect, [new PointF(0, 0), new PointF(1, 0)]));
    }

    [Fact]
    public void Null_points_are_rejected_by_name()
    {
        var matrix = new Matrix();
        Assert.Equal("pts", Assert.Throws<ArgumentNullException>(() => matrix.TransformPoints((PointF[])null!)).ParamName);
    }
}
