using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Scaling a control and its children, as AutoScaleMode does when a form's
/// font differs from the one it was designed with.
/// </summary>
public partial class Control
{
    /// <summary>Scales the control and its children; the location only if the control is not top level.</summary>
    public void Scale(SizeF factor)
    {
        SuspendLayout();

        try
        {
            ScaleControl(factor, _requiredScaling);
            ScaleChildControls(factor);
        }
        finally
        {
            ResumeLayout();
        }
    }

    public void Scale(float ratio) => Scale(new SizeF(ratio, ratio));

    public void Scale(float dx, float dy) => Scale(new SizeF(dx, dy));

    private void ScaleChildControls(SizeF factor)
    {
        if (!ScaleChildren) return;

        foreach (var child in ChildrenSnapshot()) child.Scale(factor);
    }

    /// <summary>Whether scaling this control scales its children too.</summary>
    protected virtual bool ScaleChildren => true;

    /// <summary>Scales the bounds the user set (the others come from layout and follow by themselves).</summary>
    protected virtual void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        var minimum = MinimumSize;
        var maximum = MaximumSize;

        // The constraints scale too, or they would undo the scaling.
        if (!minimum.IsEmpty) _layout.MinimumSize = ScaleSize(minimum, factor);
        if (!maximum.IsEmpty) _layout.MaximumSize = ScaleSize(maximum, factor);

        var scaled = GetScaledBounds(Bounds, factor, specified);
        if (scaled != Bounds) SetBoundsCore(scaled.X, scaled.Y, scaled.Width, scaled.Height, BoundsSpecified.All);

        _requiredScaling = BoundsSpecified.None;
    }

    /// <summary>Obsolete scaling entry point of .NET 1.x; kept for controls that override it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void ScaleCore(float dx, float dy) => ScaleControl(new SizeF(dx, dy), BoundsSpecified.All);

    /// <summary>
    /// The bounds scaled by the factor: the position of a child, and the
    /// client part of the size; the frame keeps its width, as a border does
    /// not grow with the font.
    /// </summary>
    protected virtual Rectangle GetScaledBounds(Rectangle bounds, SizeF factor, BoundsSpecified specified)
    {
        var frame = NonClient;
        var scaledX = bounds.X;
        var scaledY = bounds.Y;

        if (!GetTopLevel())
        {
            if ((specified & BoundsSpecified.X) != 0) scaledX = (int)Math.Round(bounds.X * factor.Width);
            if ((specified & BoundsSpecified.Y) != 0) scaledY = (int)Math.Round(bounds.Y * factor.Height);
        }

        var width = bounds.Width;
        var height = bounds.Height;

        if ((specified & BoundsSpecified.Width) != 0 && !GetStyle(ControlStyles.FixedWidth))
        {
            width = (int)Math.Round((bounds.Width - frame.Horizontal) * factor.Width) + frame.Horizontal;
        }

        if ((specified & BoundsSpecified.Height) != 0 && !GetStyle(ControlStyles.FixedHeight))
        {
            height = (int)Math.Round((bounds.Height - frame.Vertical) * factor.Height) + frame.Vertical;
        }

        return new Rectangle(scaledX, scaledY, width, height);
    }

    private static Size ScaleSize(Size size, SizeF factor) => new((int)Math.Round(size.Width * factor.Width), (int)Math.Round(size.Height * factor.Height));
}
