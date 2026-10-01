using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>NumericUpDown: Value's range, its spin buttons and arrow keys, its text formatting, and BeginInit/EndInit.</summary>
public class NumericUpDownTests
{
    private static Form Show(params Control[] controls)
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        form.Controls.AddRange(controls);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static void Press(Form form, PhysicalKey key)
    {
        form.Host!.KeyPressQwerty(key, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static void Type(Form form, string text)
    {
        foreach (var character in text) form.Host!.KeyTextInput(character.ToString());
    }

    /// <summary>Clicking inside the edit area (left of the 16px button column) is what focuses it, exactly as a real click would.</summary>
    private static void ClickEdit(Form form, NumericUpDown numericUpDown)
    {
        var x = numericUpDown.Left + 10;
        var y = numericUpDown.Top + numericUpDown.Height / 2;
        form.Host!.MouseDown(new Avalonia.Point(x, y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(x, y), MouseButton.Left);
    }

    private static void ClickSpinButton(Form form, NumericUpDown numericUpDown, bool up)
    {
        var x = numericUpDown.Left + numericUpDown.Width - 8;
        var y = numericUpDown.Top + (up ? numericUpDown.Height / 4 : numericUpDown.Height * 3 / 4);
        form.Host!.MouseDown(new Avalonia.Point(x, y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(x, y), MouseButton.Left);
    }

    /// <summary>OnMouseWheel is protected, exactly as Microsoft declares it; a real wheel notch is 120 units of Delta.</summary>
    private static void SpinWithWheel(NumericUpDown numericUpDown, int delta)
    {
        var method = typeof(Control).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = new MouseEventArgs(MouseButtons.None, 0, 0, 0, delta);
        method.Invoke(numericUpDown, [args]);
    }

    // --- Minimum/Maximum/Value: clamping and the exception a direct out-of-range assignment throws ---

    [Fact]
    public void Values_Value_outside_Minimum_and_Maximum_throws()
    {
        var numericUpDown = new NumericUpDown { Minimum = 0, Maximum = 10 };

        Assert.Throws<ArgumentOutOfRangeException>(() => numericUpDown.Value = 11);
        Assert.Throws<ArgumentOutOfRangeException>(() => numericUpDown.Value = -1);
    }

    [Fact]
    public void Raising_Minimum_above_Value_clamps_it_and_lowering_Maximum_too()
    {
        var numericUpDown = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 50 };

        numericUpDown.Minimum = 60;
        Assert.Equal(60, numericUpDown.Value);
        Assert.Equal(60, numericUpDown.Minimum);

        numericUpDown.Maximum = 55;
        Assert.Equal(55, numericUpDown.Value);
        Assert.Equal(55, numericUpDown.Minimum);
    }

    // --- UpButton/DownButton: direct calls, and the same thing driven by headless buttons and keys ---

    [Fact]
    public void UpButton_and_DownButton_step_by_Increment_and_clamp_at_the_bounds()
    {
        var numericUpDown = new NumericUpDown { Minimum = 0, Maximum = 10, Increment = 3, Value = 9 };
        var changes = new List<decimal>();
        numericUpDown.ValueChanged += (_, _) => changes.Add(numericUpDown.Value);

        numericUpDown.UpButton();
        Assert.Equal(10, numericUpDown.Value); // 9 + 3 would overshoot: clamped at Maximum.

        numericUpDown.DownButton();
        numericUpDown.DownButton();
        numericUpDown.DownButton();
        Assert.Equal(1, numericUpDown.Value);

        numericUpDown.DownButton();
        Assert.Equal(0, numericUpDown.Value); // 1 - 3 would undershoot: clamped at Minimum.

        Assert.Equal([10m, 7m, 4m, 1m, 0m], changes);
    }

    [AvaloniaFact]
    public void Clicking_the_up_and_down_buttons_steps_the_value()
    {
        var numericUpDown = new NumericUpDown { Bounds = new Rectangle(10, 10, 100, 24), Minimum = 0, Maximum = 10, Value = 5 };
        var form = Show(numericUpDown);

        ClickSpinButton(form, numericUpDown, up: true);
        Assert.Equal(6, numericUpDown.Value);

        ClickSpinButton(form, numericUpDown, up: false);
        ClickSpinButton(form, numericUpDown, up: false);
        Assert.Equal(4, numericUpDown.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Arrow_keys_on_the_focused_edit_step_the_value()
    {
        var numericUpDown = new NumericUpDown { Bounds = new Rectangle(10, 10, 100, 24), Minimum = 0, Maximum = 10, Value = 5 };
        var form = Show(numericUpDown);
        ClickEdit(form, numericUpDown);

        Press(form, PhysicalKey.ArrowUp);
        Assert.Equal(6, numericUpDown.Value);

        Press(form, PhysicalKey.ArrowDown);
        Press(form, PhysicalKey.ArrowDown);
        Assert.Equal(4, numericUpDown.Value);

        form.Close();
    }

    [Fact]
    public void The_mouse_wheel_steps_the_value()
    {
        var numericUpDown = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 5 };

        SpinWithWheel(numericUpDown, 120);
        Assert.Equal(6, numericUpDown.Value);

        SpinWithWheel(numericUpDown, -120);
        SpinWithWheel(numericUpDown, -120);
        Assert.Equal(4, numericUpDown.Value);
    }

    // --- Text formatting: decimal places, thousands separators and hexadecimal ---

    [Fact]
    public void DecimalPlaces_and_ThousandsSeparator_format_the_displayed_text()
    {
        // The format is culture-sensitive, as Microsoft's is; fixed to one culture so the assertion is deterministic.
        var original = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        try
        {
            var numericUpDown = new NumericUpDown { Minimum = 0, Maximum = 1_000_000, DecimalPlaces = 2, Value = 1234.5m };

            Assert.Equal("1234.50", numericUpDown.Text);

            numericUpDown.ThousandsSeparator = true;
            Assert.Equal("1,234.50", numericUpDown.Text);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Hexadecimal_formats_the_displayed_value_as_upper_case_hex()
    {
        var numericUpDown = new NumericUpDown { Minimum = 0, Maximum = 1000, Hexadecimal = true, Value = 255 };

        Assert.Equal("FF", numericUpDown.Text);
    }

    // --- Parsing: on Enter, on leaving the edit, and invalid text reverting ---

    [AvaloniaFact]
    public void Typing_and_pressing_Enter_parses_the_text_into_Value()
    {
        var numericUpDown = new NumericUpDown { Bounds = new Rectangle(10, 10, 100, 24), Minimum = 0, Maximum = 100, Value = 0 };
        var form = Show(numericUpDown);
        ClickEdit(form, numericUpDown);

        Type(form, "42");
        Press(form, PhysicalKey.Enter);

        Assert.Equal(42, numericUpDown.Value);
        Assert.Equal("42", numericUpDown.Text);

        form.Close();
    }

    [AvaloniaFact]
    public void Typing_and_then_leaving_the_edit_parses_the_text_into_Value()
    {
        var numericUpDown = new NumericUpDown { Bounds = new Rectangle(10, 10, 100, 24), Minimum = 0, Maximum = 100, Value = 0 };
        var other = new TextBox { Bounds = new Rectangle(10, 60, 100, 20) };
        var form = Show(numericUpDown, other);
        ClickEdit(form, numericUpDown);

        Type(form, "37");
        other.Focus();

        Assert.Equal(37, numericUpDown.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Invalid_typed_text_reverts_to_the_last_valid_display_on_Enter()
    {
        var numericUpDown = new NumericUpDown { Bounds = new Rectangle(10, 10, 100, 24), Minimum = 0, Maximum = 100, Value = 20, Hexadecimal = false };
        var form = Show(numericUpDown);
        ClickEdit(form, numericUpDown);

        // Only digits (and the decimal separator, with DecimalPlaces 0) are accepted; "20" stays "20".
        Type(form, "x");
        Press(form, PhysicalKey.Enter);

        Assert.Equal(20, numericUpDown.Value);
        Assert.Equal("20", numericUpDown.Text);

        form.Close();
    }

    // --- BeginInit/EndInit: ISupportInitialize defers Value's range check until EndInit ---

    [Fact]
    public void Without_BeginInit_an_out_of_range_Value_throws()
    {
        var numericUpDown = new NumericUpDown();

        Assert.Throws<ArgumentOutOfRangeException>(() => numericUpDown.Value = 500);
    }

    [Fact]
    public void BeginInit_defers_Values_range_check_until_EndInit()
    {
        var numericUpDown = new NumericUpDown();

        numericUpDown.BeginInit();
        numericUpDown.Value = 500; // Above the default Maximum of 100: would throw without BeginInit.
        numericUpDown.Maximum = 1000;
        numericUpDown.EndInit();

        Assert.Equal(500, numericUpDown.Value);
    }

    [Fact]
    public void EndInit_clamps_a_value_still_out_of_range_once_initialization_ends()
    {
        var numericUpDown = new NumericUpDown();

        numericUpDown.BeginInit();
        numericUpDown.Value = 500; // Maximum is never raised this time.
        numericUpDown.EndInit();

        Assert.Equal(100, numericUpDown.Value); // Clamped to the default Maximum, not left dangling above it.
    }

    // --- NumericUpDownAccelerationCollection: the plain collection NumericUpDown.Accelerations exposes ---

    [Fact]
    public void The_acceleration_collection_adds_counts_and_removes()
    {
        var numericUpDown = new NumericUpDown();
        var first = new NumericUpDownAcceleration(1, 5m);
        var second = new NumericUpDownAcceleration(3, 25m);

        numericUpDown.Accelerations.AddRange(first, second);

        Assert.Equal(2, numericUpDown.Accelerations.Count);
        Assert.Equal(first, numericUpDown.Accelerations[0]);
        Assert.Contains(second, numericUpDown.Accelerations);

        Assert.True(numericUpDown.Accelerations.Remove(first));
        Assert.Single(numericUpDown.Accelerations);
    }

    [AvaloniaFact]
    public void A_held_spin_button_stops_repeating_when_the_capture_is_lost()
    {
        var numericUpDown = new NumericUpDown { Bounds = new System.Drawing.Rectangle(10, 10, 120, 20), Maximum = 1000 };
        var form = Show(numericUpDown);

        // The up button is the upper half of the 16-pixel strip at the right edge.
        form.Host!.MouseDown(new Avalonia.Point(10 + 120 - 8, 10 + 5), MouseButton.Left);
        var afterPress = numericUpDown.Value;
        numericUpDown.Capture = false;

        using var loop = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
        Dispatcher.UIThread.MainLoop(loop.Token);

        Assert.Equal(afterPress, numericUpDown.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Focusing_the_box_raises_GotFocus_on_the_up_down_control()
    {
        var numericUpDown = new NumericUpDown { Bounds = new System.Drawing.Rectangle(10, 10, 120, 20) };
        var other = new Button { Location = new System.Drawing.Point(10, 50) };
        var form = Show(other, numericUpDown);
        other.Focus();
        var gotFocus = 0;
        numericUpDown.GotFocus += (_, _) => gotFocus++;

        numericUpDown.Focus();

        Assert.Equal(1, gotFocus);

        form.Close();
    }

    [Fact]
    public void A_hexadecimal_box_with_a_range_beyond_64_bits_shows_its_value_without_throwing()
    {
        var numericUpDown = new NumericUpDown { Minimum = decimal.MinValue, Maximum = decimal.MaxValue };

        numericUpDown.Hexadecimal = true;
        numericUpDown.Value = decimal.MinValue;

        Assert.False(string.IsNullOrEmpty(numericUpDown.Text));
    }
}
