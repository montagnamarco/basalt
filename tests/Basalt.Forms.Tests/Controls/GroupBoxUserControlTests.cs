using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>GroupBox's frame and caption, and UserControl's border and Load event.</summary>
public class GroupBoxUserControlTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    private static bool InvokeMnemonic(Control control, char character) =>
        (bool)typeof(Control).GetMethod("ProcessMnemonic", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(control, [character])!;

    // --- GroupBox -----------------------------------------------------------

    [Fact]
    public void DisplayRectangle_is_inset_3_pixels_except_the_top_which_is_the_captions_line_height()
    {
        var groupBox = new GroupBox { Font = Font, Size = new Size(200, 100) };
        var fontHeight = groupBox.Font.Height;

        var expected = new Rectangle(3, fontHeight, 194, 100 - fontHeight - 3);

        Assert.Equal(expected, groupBox.DisplayRectangle);
    }

    [Fact]
    public void The_caption_sits_8_pixels_in_and_covers_the_border_line_under_it()
    {
        var groupBox = new GroupBox { Text = "Group", Font = Font, BackColor = Color.White, Size = new Size(200, 100) };
        using var bitmap = Draw(groupBox);

        var textHeight = TextRenderer.MeasureText(groupBox.Text, groupBox.Font).Height;
        var frameTop = textHeight / 2;

        // Before the caption's 8-pixel offset, the etched frame's top edge still shows.
        Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(2, frameTop).ToArgb());

        // At the offset itself, the caption's background fill shows instead of the border line.
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(8, frameTop).ToArgb());

        // Well past the caption, the frame's top edge shows again.
        Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(190, frameTop).ToArgb());
    }

    [Fact]
    public void An_auto_sized_GroupBox_grows_to_fit_its_children()
    {
        var groupBox = new GroupBox { Font = Font, AutoSize = true };
        var child = new Control { Bounds = new Rectangle(10, 10, 80, 25) };

        groupBox.Controls.Add(child);

        // 90 (child's right plus its margin) + 3 each side; the caption's line height cancels out of the height.
        Assert.Equal(new Size(96, 41), groupBox.PreferredSize);
    }

    [Fact]
    public void The_mnemonic_selects_the_group_boxs_first_child()
    {
        var groupBox = new GroupBox { Text = "&Group" };
        var first = new Button { TabIndex = 0 };
        var second = new Button { TabIndex = 1 };
        groupBox.Controls.Add(first);
        groupBox.Controls.Add(second);
        var container = new ContainerControl();
        container.Controls.Add(groupBox);

        Assert.True(InvokeMnemonic(groupBox, 'g'));
        Assert.Same(first, container.ActiveControl);

        Assert.False(InvokeMnemonic(groupBox, 'x'));
    }

    [Fact]
    public void A_GroupBox_cannot_be_selected_and_is_not_a_tab_stop()
    {
        var groupBox = new GroupBox();

        Assert.False(groupBox.CanSelect);
        Assert.False(groupBox.TabStop);
    }

    // --- UserControl ----------------------------------------------------------

    [Fact]
    public void UserControl_borders_shrink_the_client_area_by_their_width()
    {
        var sunken = new UserControl { BorderStyle = BorderStyle.Fixed3D, Size = new Size(100, 60) };
        var single = new UserControl { BorderStyle = BorderStyle.FixedSingle, Size = new Size(100, 60) };

        Assert.Equal(new Size(96, 56), sunken.ClientSize);
        Assert.Equal(new Size(98, 58), single.ClientSize);
    }

    [AvaloniaFact]
    public void Load_is_raised_once_the_first_time_the_control_is_shown()
    {
        var control = new UserControl();
        var loads = 0;
        control.Load += (_, _) => loads++;

        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, loads);

        control.Visible = false;
        control.Visible = true;

        Assert.Equal(1, loads);

        form.Close();
    }
}
