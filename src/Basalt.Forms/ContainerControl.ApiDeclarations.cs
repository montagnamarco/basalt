using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

public partial class ContainerControl
{
    protected override void RescaleConstantsForDpi(int oldDpi, int newDpi) => base.RescaleConstantsForDpi(oldDpi, newDpi);

    /// <summary>Scales constraints independently when the host has already supplied the new bounds.</summary>
    protected virtual void ScaleMinMaxSize(float xScaleFactor, float yScaleFactor, bool updateContainerSize = true)
    {
        if (!float.IsFinite(xScaleFactor) || xScaleFactor <= 0 || !float.IsFinite(yScaleFactor) || yScaleFactor <= 0)
            throw new ArgumentOutOfRangeException(nameof(xScaleFactor));
        var minimum = MinimumSize;
        var maximum = MaximumSize;
        Size Scaled(Size size) => new((int)Math.Round(size.Width * xScaleFactor), (int)Math.Round(size.Height * yScaleFactor));
        SuspendLayout();
        try
        {
            var store = ((IArrangedElement)this).LayoutStore;
            store.MinimumSize = Scaled(minimum);
            store.MaximumSize = Scaled(maximum);
            if (updateContainerSize)
            {
                var size = ApplySizeConstraints(Size);
                SetBoundsCore(Left, Top, size.Width, size.Height, BoundsSpecified.All);
            }
        }
        finally
        {
            ResumeLayout();
        }
    }
}
