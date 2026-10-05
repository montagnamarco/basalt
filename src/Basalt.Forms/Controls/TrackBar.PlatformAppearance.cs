using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public partial class TrackBar
{
    /// <summary>Reproduces the ~16px diameter of a macOS/GTK round slider knob
    /// (roughly the height of a standard small control), capped to the track's
    /// own thickness by <see cref="RoundThumbDiameter"/> below.</summary>
    private const int PortableRoundThumbDiameter = 16;

    private bool UsePortableRoundThumb => PortableVisualStylesEnabled
        && PortableVisualStylePlatform is PortableVisualStylePlatform.MacOS or PortableVisualStylePlatform.Linux;

    private int RoundThumbDiameter(Rectangle area) => Math.Min(PortableRoundThumbDiameter,
        Math.Max(_orientation == Orientation.Horizontal ? area.Height : area.Width, 0));

    private void PaintPortableTrack(Graphics graphics, Rectangle groove)
    {
        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        using var track = new SolidBrush(palette.Border);
        using var fill = new SolidBrush(Enabled ? palette.Accent : palette.DisabledText);
        graphics.FillRectangle(track, groove);
        if (_orientation == Orientation.Horizontal)
        {
            graphics.FillRectangle(fill, groove.Left, groove.Top, (int)(groove.Width * FractionOf(_value)), groove.Height);
        }
        else
        {
            var height = (int)(groove.Height * (1 - FractionOf(_value)));
            graphics.FillRectangle(fill, groove.Left, groove.Bottom - height, groove.Width, height);
        }
    }

    private void PaintPortableThumb(Graphics graphics, Rectangle thumb)
    {
        if (thumb.Width <= 0 || thumb.Height <= 0) return;

        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        if (UsePortableRoundThumb)
        {
            var faceColor = !Enabled ? palette.Face : _dragging ? palette.PressedFace : palette.Field;
            using var face = new SolidBrush(faceColor);
            using var border = new Pen(Focused ? palette.Accent : palette.Border);
            graphics.FillEllipse(face, thumb);
            graphics.DrawEllipse(border, thumb.X, thumb.Y, thumb.Width - 1, thumb.Height - 1);
            return;
        }

        // The thumb is never the form's default button; Focused only wants the same
        // accent emphasis a default button gets, so it goes through emphasized, not isDefault.
        PortableVisualStyles.PaintButton(graphics, thumb, palette, PortableVisualStylePlatform,
            hot: Focused, pressed: _dragging, enabled: Enabled, isDefault: false, emphasized: Focused);
    }
}
