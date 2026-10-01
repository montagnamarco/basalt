using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>CheckBox and RadioButton: state cycles, events, radio group exclusivity, Normal-appearance painting and AutoSize.</summary>
public class CheckBoxRadioButtonTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    /// <summary>Presses and releases the left button over the control's top-left corner, the gesture a push button reacts to.</summary>
    private static void Click(Form form, Control control)
    {
        var point = new Avalonia.Point(control.Left + 5, control.Top + 5);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [AvaloniaFact]
    public void Clicking_cycles_a_three_state_checkbox_through_indeterminate_and_back()
    {
        var checkBox = new CheckBox { ThreeState = true, Bounds = new Rectangle(10, 10, 104, 24) };
        var form = ShowWith(checkBox);

        Assert.Equal(CheckState.Unchecked, checkBox.CheckState);

        Click(form, checkBox);
        Assert.Equal(CheckState.Checked, checkBox.CheckState);

        Click(form, checkBox);
        Assert.Equal(CheckState.Indeterminate, checkBox.CheckState);

        Click(form, checkBox);
        Assert.Equal(CheckState.Unchecked, checkBox.CheckState);

        form.Close();
    }

    [AvaloniaFact]
    public void Without_ThreeState_a_click_only_toggles_between_unchecked_and_checked()
    {
        var checkBox = new CheckBox { Bounds = new Rectangle(10, 10, 104, 24) };
        var form = ShowWith(checkBox);

        Click(form, checkBox);
        Assert.Equal(CheckState.Checked, checkBox.CheckState);

        Click(form, checkBox);
        Assert.Equal(CheckState.Unchecked, checkBox.CheckState);

        form.Close();
    }

    [AvaloniaFact]
    public void AutoCheck_false_leaves_the_state_alone_but_still_clicks()
    {
        var checkBox = new CheckBox { AutoCheck = false, Bounds = new Rectangle(10, 10, 104, 24) };
        var form = ShowWith(checkBox);
        var clicks = 0;
        checkBox.Click += (_, _) => clicks++;

        Click(form, checkBox);

        Assert.Equal(CheckState.Unchecked, checkBox.CheckState);
        Assert.Equal(1, clicks);

        form.Close();
    }

    [AvaloniaFact]
    public void A_click_raises_CheckedChanged_then_CheckStateChanged_before_Click()
    {
        var checkBox = new CheckBox { Bounds = new Rectangle(10, 10, 104, 24) };
        var form = ShowWith(checkBox);
        var order = new List<string>();
        checkBox.CheckStateChanged += (_, _) => order.Add("CheckStateChanged");
        checkBox.CheckedChanged += (_, _) => order.Add("CheckedChanged");
        checkBox.Click += (_, _) => order.Add("Click");

        Click(form, checkBox);

        Assert.Equal(["CheckedChanged", "CheckStateChanged", "Click"], order);

        form.Close();
    }

    [Fact]
    public void Setting_CheckState_to_Indeterminate_does_not_raise_CheckedChanged_from_Checked()
    {
        var checkBox = new CheckBox { Checked = true };
        var checkedChanges = 0;
        checkBox.CheckedChanged += (_, _) => checkedChanges++;

        checkBox.CheckState = CheckState.Indeterminate;

        // Checked (CheckState != Unchecked) was already true and stays true: no change to report.
        Assert.True(checkBox.Checked);
        Assert.Equal(0, checkedChanges);
    }

    [AvaloniaFact]
    public void Space_toggles_a_focused_checkbox_on_release()
    {
        var checkBox = new CheckBox();
        var form = ShowWith(checkBox);
        checkBox.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.False(checkBox.Checked);

        form.Host!.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.True(checkBox.Checked);

        form.Close();
    }

    [Fact]
    public void A_checked_box_draws_a_glyph_the_unchecked_box_does_not()
    {
        var unchecked_ = new CheckBox { Size = new Size(104, 24), BackColor = Color.White };
        var checked_ = new CheckBox { Size = new Size(104, 24), BackColor = Color.White, Checked = true };

        using var uncheckedBitmap = Draw(unchecked_);
        using var checkedBitmap = Draw(checked_);

        // (7, 11) sits on the check mark's second stroke; the unchecked box
        // leaves that pixel as the glyph's plain window-coloured interior.
        Assert.Equal(SystemColors.Window.ToArgb(), uncheckedBitmap.GetPixel(7, 11).ToArgb());
        Assert.NotEqual(SystemColors.Window.ToArgb(), checkedBitmap.GetPixel(7, 11).ToArgb());
    }

    [Fact]
    public void A_button_appearance_checkbox_stays_sunken_while_checked()
    {
        var checkBox = new CheckBox { Appearance = Appearance.Button, Size = new Size(104, 24), Checked = true };

        using var bitmap = Draw(checkBox);

        // ButtonBase's PaintButton draws the dark top-left edge only when Pushed is true.
        Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void An_auto_sized_checkboxs_width_grows_by_exactly_the_added_text_width()
    {
        var checkBox = new CheckBox { Font = Font, AutoSize = true, Text = "Hi" };
        var first = checkBox.Width;

        checkBox.Text = "Hi there";

        var delta = checkBox.Width - first;
        var textDelta = TextRenderer.MeasureText("Hi there", Font).Width - TextRenderer.MeasureText("Hi", Font).Width;

        Assert.Equal(textDelta, delta);
        Assert.True(checkBox.Width > TextRenderer.MeasureText("Hi there", Font).Width, "Expected room for the glyph as well as the text.");

        var expectedHeight = Math.Max(TextRenderer.MeasureText("Hi there", Font).Height, 13);
        Assert.Equal(expectedHeight, checkBox.Height);
    }

    [Fact]
    public void Checking_a_radio_button_unchecks_its_autocheck_siblings_in_the_same_parent()
    {
        var panel = new Panel();
        var first = new RadioButton { Parent = panel };
        var second = new RadioButton { Parent = panel };
        var third = new RadioButton { Parent = panel, AutoCheck = false };

        first.Checked = true;
        Assert.True(first.Checked);

        second.Checked = true;
        Assert.False(first.Checked);
        Assert.True(second.Checked);

        // Third opted out of the group with AutoCheck = false: checking it leaves
        // the others alone, and the others leave it alone.
        third.Checked = true;
        Assert.True(second.Checked);
        Assert.True(third.Checked);

        first.Checked = true;
        Assert.True(first.Checked);
        Assert.True(third.Checked, "Third has AutoCheck = false, so other radio buttons do not uncheck it.");
    }

    [Fact]
    public void A_radio_button_only_tab_stops_while_it_is_checked()
    {
        var radio = new RadioButton();
        Assert.False(radio.TabStop);

        radio.Checked = true;
        Assert.True(radio.TabStop);

        radio.Checked = false;
        Assert.False(radio.TabStop);
    }

    [AvaloniaFact]
    public void Clicking_a_radio_button_checks_it_and_raises_CheckedChanged_before_Click()
    {
        var radio = new RadioButton { Bounds = new Rectangle(10, 10, 104, 24) };
        var form = ShowWith(radio);
        var order = new List<string>();
        radio.CheckedChanged += (_, _) => order.Add("CheckedChanged");
        radio.Click += (_, _) => order.Add("Click");

        Click(form, radio);

        Assert.True(radio.Checked);
        Assert.Equal(["CheckedChanged", "Click"], order);

        form.Close();
    }

    [Fact]
    public void A_radio_buttons_mnemonic_checks_it()
    {
        var radio = new RadioButton { Text = "&Yes" };

        Assert.True(InvokeMnemonic(radio, 'y'));
        Assert.True(radio.Checked);

        Assert.False(InvokeMnemonic(radio, 'x'));
    }

    private static bool InvokeMnemonic(Control control, char character) =>
        (bool)typeof(Control).GetMethod("ProcessMnemonic", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, [character])!;

    [Fact]
    public void An_auto_sized_check_box_grows_with_its_font_inside_a_parent()
    {
        var panel = new Panel { Size = new Size(300, 100) };
        var checkBox = new CheckBox { AutoSize = true, Text = "Option", Parent = panel };
        var before = checkBox.Size;

        checkBox.Font = new Font("Microsoft Sans Serif", 14f);

        Assert.True(checkBox.Width > before.Width && checkBox.Height > before.Height, $"{before} -> {checkBox.Size}");
    }
}
