using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Components;

/// <summary>
/// ErrorProvider's text, its icon beside the control, and the errors it
/// reads from a bound <see cref="IDataErrorInfo"/>.
/// </summary>
/// <remarks>
/// There is no Windows reference to compare the icon's exact pixel offset
/// against: the alignment and padding formulas in ErrorProvider.cs are our
/// own reading of the documented behaviour, not verified against a real
/// Windows ErrorProvider.
/// </remarks>
public class ErrorProviderTests
{
    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    /// <summary>True on the red disc Basalt.Drawing's SystemIcons.Error paints, false on the plain background or on the white cross through its middle.</summary>
    private static bool IsErrorRed(Color pixel) => pixel.R > 150 && pixel.G < 80 && pixel.B < 80;

    [Fact]
    public void SetError_shows_the_text_and_an_empty_value_clears_it()
    {
        var target = new Control();
        using var provider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };

        Assert.Equal(string.Empty, provider.GetError(target));
        Assert.False(provider.HasErrors);

        provider.SetError(target, "Required");

        Assert.Equal("Required", provider.GetError(target));
        Assert.True(provider.HasErrors);

        provider.SetError(target, "");

        Assert.Equal(string.Empty, provider.GetError(target));
        Assert.False(provider.HasErrors);
        Assert.False(provider.IsIconVisible(target));
    }

    [Fact]
    public void SetIconAlignment_and_SetIconPadding_read_back_what_was_set()
    {
        var target = new Control();
        using var provider = new ErrorProvider();

        Assert.Equal(ErrorIconAlignment.MiddleRight, provider.GetIconAlignment(target));
        Assert.Equal(0, provider.GetIconPadding(target));

        provider.SetIconAlignment(target, ErrorIconAlignment.TopLeft);
        provider.SetIconPadding(target, 7);

        Assert.Equal(ErrorIconAlignment.TopLeft, provider.GetIconAlignment(target));
        Assert.Equal(7, provider.GetIconPadding(target));
    }

    /// <summary>
    /// Our placement: the icon's 16x16 box sits just outside the control, on
    /// the alignment's side, padding pixels further away, and centred on the
    /// control's height for Middle alignments. The sample point avoids the
    /// icon's own white cross, which crosses exactly through its centre.
    /// </summary>
    [Fact]
    public void The_icon_sits_beside_the_control_by_alignment_and_padding()
    {
        var parent = new Control { Size = new Size(200, 120), BackColor = Color.White };
        var target = new Control { Bounds = new Rectangle(60, 40, 60, 20), BackColor = Color.White, Parent = parent };

        using var provider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };
        provider.SetIconPadding(target, 4);
        provider.SetError(target, "Required");

        var rightIconTop = target.Top + (target.Height - 16) / 2;
        var rightSample = new Point(target.Right + 4 + 3, rightIconTop + 12);

        using (var bitmap = Draw(parent))
        {
            Assert.True(IsErrorRed(bitmap.GetPixel(rightSample.X, rightSample.Y)),
                $"Expected the red error icon at {rightSample}, got {bitmap.GetPixel(rightSample.X, rightSample.Y)}.");
        }

        // Moving the alignment to the left moves the icon there instead, and it leaves its former place.
        provider.SetIconAlignment(target, ErrorIconAlignment.MiddleLeft);

        var leftIconTop = rightIconTop;
        var leftSample = new Point(target.Left - 4 - 16 + 3, leftIconTop + 12);

        using var moved = Draw(parent);

        Assert.True(IsErrorRed(moved.GetPixel(leftSample.X, leftSample.Y)),
            $"Expected the red error icon at {leftSample}, got {moved.GetPixel(leftSample.X, leftSample.Y)}.");
        Assert.False(IsErrorRed(moved.GetPixel(rightSample.X, rightSample.Y)),
            "The icon should have left its former place on the right.");
    }

    [AvaloniaFact]
    public void AlwaysBlink_toggles_the_icon_and_settles_it_visible()
    {
        var parent = new Control();
        var target = new Control { Parent = parent };
        using var provider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.AlwaysBlink };

        provider.SetError(target, "Bad value");
        Assert.True(provider.IsIconVisible(target));

        var sawInvisible = false;
        for (var i = 0; i < 10; i++)
        {
            provider.SimulateBlinkTick();
            if (!provider.IsIconVisible(target)) sawInvisible = true;
        }

        Assert.True(sawInvisible, "Expected the icon to blink off at least once.");
        Assert.True(provider.IsIconVisible(target), "Expected the icon to settle visible once the blink phases run out.");
    }

    [Fact]
    public void NeverBlink_keeps_the_icon_steady()
    {
        var parent = new Control();
        var target = new Control { Parent = parent };
        using var provider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };

        provider.SetError(target, "Bad value");

        for (var i = 0; i < 10; i++)
        {
            provider.SimulateBlinkTick();
            Assert.True(provider.IsIconVisible(target));
        }
    }

    [AvaloniaFact]
    public void BlinkIfDifferentError_blinks_only_when_the_text_changes()
    {
        var parent = new Control();
        var target = new Control { Parent = parent };
        using var provider = new ErrorProvider(); // BlinkIfDifferentError is the default.

        provider.SetError(target, "Required");
        for (var i = 0; i < 10; i++) provider.SimulateBlinkTick();
        Assert.True(provider.IsIconVisible(target));

        // Re-setting the very same text does not start a new blink sequence.
        provider.SetError(target, "Required");
        provider.SimulateBlinkTick();
        Assert.True(provider.IsIconVisible(target), "Setting the same error text again should not blink.");

        // A different text does.
        provider.SetError(target, "Still wrong");
        provider.SimulateBlinkTick();
        Assert.False(provider.IsIconVisible(target), "A new, different error text should start blinking again.");
    }

    [Fact]
    public void Clear_removes_every_error_and_icon()
    {
        var parent = new Control();
        var target = new Control { Parent = parent };
        using var provider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };

        provider.SetError(target, "Required");
        Assert.True(provider.HasErrors);

        provider.Clear();

        Assert.False(provider.HasErrors);
        Assert.Equal(string.Empty, provider.GetError(target));
        Assert.False(provider.IsIconVisible(target));
    }

    private sealed class Person : IDataErrorInfo
    {
        public string? Name { get; set; }

        public string Error => string.Empty;

        public string this[string columnName] => columnName == "Name" && string.IsNullOrEmpty(Name) ? "Name is required." : string.Empty;
    }

    [Fact]
    public void Binding_to_a_data_source_reads_errors_from_the_current_items_IDataErrorInfo()
    {
        var people = new List<Person> { new() { Name = "" } };
        var container = new ContainerControl();
        var textBox = new Control();
        container.Controls.Add(textBox);
        textBox.DataBindings.Add("Text", people, "Name");

        using var provider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink, ContainerControl = container };
        provider.DataSource = people;

        Assert.Equal("Name is required.", provider.GetError(textBox));

        // Nothing but IDataErrorInfo reports this; UpdateBinding re-reads it,
        // as a Form would after the current record is validated.
        people[0].Name = "Ada";
        provider.UpdateBinding();

        Assert.Equal(string.Empty, provider.GetError(textBox));
    }

    [Fact]
    public void A_zero_BlinkRate_means_the_icon_never_blinks()
    {
        using var provider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.AlwaysBlink };

        provider.BlinkRate = 0;
        Assert.Equal(ErrorBlinkStyle.NeverBlink, provider.BlinkStyle);

        provider.BlinkStyle = ErrorBlinkStyle.AlwaysBlink;
        Assert.Equal(ErrorBlinkStyle.NeverBlink, provider.BlinkStyle);
    }

    // On the UI thread: SetError creates the icon's tooltip and blink timer, which start Avalonia.
    [AvaloniaFact]
    public void The_icon_follows_its_control_to_a_new_parent()
    {
        var first = new Panel();
        var second = new Panel();
        var box = new TextBox { Parent = first };
        using var provider = new ErrorProvider();
        provider.SetError(box, "Required");
        Assert.Equal(2, first.Controls.Count);

        box.Parent = second;

        Assert.Empty(first.Controls.Cast<Control>());
        Assert.Equal(2, second.Controls.Count);
    }
}
