using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>ButtonBase.FlatAppearance: the colours FlatStyle.Flat/Popup paint for hover, mouse-down and the border.</summary>
public class ButtonFlatAppearanceTests
{
    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [AvaloniaFact]
    public void MouseOverBackColor_paints_once_the_pointer_enters_a_flat_button()
    {
        var button = new Button { FlatStyle = FlatStyle.Flat, Bounds = new Rectangle(10, 10, 80, 30) };
        button.FlatAppearance.MouseOverBackColor = Color.Lime;
        var form = ShowWith(button);

        var before = Draw(button);
        Assert.NotEqual(Color.Lime.ToArgb(), before.GetPixel(40, 15).ToArgb());

        var point = new Avalonia.Point(button.Left + 40, button.Top + 15);
        form.Host!.MouseMove(point);
        Dispatcher.UIThread.RunJobs();

        var after = Draw(button);
        Assert.Equal(Color.Lime.ToArgb(), after.GetPixel(40, 15).ToArgb());

        form.Close();
    }

    [AvaloniaFact]
    public void MouseDownBackColor_paints_while_the_left_button_is_held()
    {
        var button = new Button { FlatStyle = FlatStyle.Flat, Bounds = new Rectangle(10, 10, 80, 30) };
        button.FlatAppearance.MouseDownBackColor = Color.Red;
        var form = ShowWith(button);

        var point = new Avalonia.Point(button.Left + 40, button.Top + 15);
        form.Host!.MouseMove(point);
        form.Host!.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var pressed = Draw(button);
        Assert.Equal(Color.Red.ToArgb(), pressed.GetPixel(40, 15).ToArgb());

        form.Host!.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var released = Draw(button);
        Assert.NotEqual(Color.Red.ToArgb(), released.GetPixel(40, 15).ToArgb());

        form.Close();
    }

    [AvaloniaFact]
    public void BorderColor_and_BorderSize_control_the_flat_edge()
    {
        var button = new Button { FlatStyle = FlatStyle.Flat, TabStop = false, Bounds = new Rectangle(10, 10, 80, 30) };
        button.FlatAppearance.BorderColor = Color.Blue;
        button.FlatAppearance.BorderSize = 3;
        var form = ShowWith(button);

        var bitmap = Draw(button);

        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(0, 15).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(2, 15).ToArgb());
        Assert.NotEqual(Color.Blue.ToArgb(), bitmap.GetPixel(4, 15).ToArgb());

        form.Close();
    }

    [AvaloniaFact]
    public void BorderSize_rejects_negative_values()
    {
        var button = new Button();

        Assert.Throws<ArgumentOutOfRangeException>(() => button.FlatAppearance.BorderSize = -1);
    }
}
