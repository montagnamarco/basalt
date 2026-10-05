using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

public partial class Form
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Rectangle DesktopBounds
    {
        get => new(DesktopLocation, Size);
        set => SetDesktopBounds(value.X, value.Y, value.Width, value.Height);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point DesktopLocation
    {
        get
        {
            var origin = SystemInformation.WorkingArea.Location;
            return new Point(Left - origin.X, Top - origin.Y);
        }
        set => SetDesktopLocation(value.X, value.Y);
    }

    public void SetDesktopBounds(int x, int y, int width, int height)
    {
        var origin = SystemInformation.WorkingArea.Location;
        SetBounds(x + origin.X, y + origin.Y, width, height);
    }

    public void SetDesktopLocation(int x, int y)
    {
        var origin = SystemInformation.WorkingArea.Location;
        Location = new Point(x + origin.X, y + origin.Y);
    }

    public override bool ValidateChildren() => base.ValidateChildren();

    public override bool ValidateChildren(ValidationConstraints validationConstraints) => base.ValidateChildren(validationConstraints);

    public override string ToString() => $"{base.ToString()}, Text: {Text}";
}
