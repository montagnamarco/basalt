using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Basalt.Tests;

/// <summary>
/// Settings &gt; Font size, and every other numeric field in the app, share
/// one style (Styles.axaml). Fluent's own NumericUpDown template measures 32
/// tall against the compact 22 every TextBox in the app uses, so a value
/// typed into one read as sitting a few pixels above the middle of the box —
/// the two controls disagreeing about their own height is what put the ink
/// off centre.
/// </summary>
public sealed class NumericUpDownAlignmentTests
{
    [AvaloniaFact]
    public void TheBoxIsAsCompactAsATextBox()
    {
        var box = new NumericUpDown
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var window = new Window { Content = box, Width = 200, Height = 80 };
        window.Show();
        for (var i = 0; i < 5; i++) window.UpdateLayout();

        var height = box.Bounds.Height;
        window.Close();

        Assert.True(height <= 23, $"NumericUpDown measured {height} tall, a TextBox measures 22");
    }

    [AvaloniaFact]
    public void DigitInkIsVerticallyCentredOnTheBox()
    {
        // A TextBox at the same compact height, in the same window, as the
        // control to compare the NumericUpDown's digit against: the Settings
        // window's "Font family" field sits right above "Font size", and
        // what makes the two read as disagreeing is the digit not landing
        // where that TextBox's own text does.
        var reference = new TextBox
        {
            Text = "13",
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var box = new NumericUpDown
        {
            Value = 13,
            Minimum = 0,
            Maximum = 48,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var panel = new StackPanel { Spacing = 10, Children = { reference, box } };
        var window = new Window { Content = panel, Width = 200, Height = 120 };
        window.Show();
        for (var i = 0; i < 5; i++) window.UpdateLayout();

        var raster = Raster.Of(window);
        var textBox = box.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_TextBox");

        var textArea = Raster.BoundsIn(textBox, window);
        var inkCenter = InkVerticalCenter(raster, textBox, textArea);
        var boxCenter = textArea.Y + textArea.Height / 2.0;

        var referenceArea = Raster.BoundsIn(reference, window);
        var referenceCenter = InkVerticalCenter(raster, reference, referenceArea);
        var referenceOffset = referenceCenter - referenceArea.Y;
        var numericOffset = inkCenter - textArea.Y;

        window.Close();

        Assert.True(Math.Abs(inkCenter - boxCenter) <= 2,
            $"the digit's ink centres at {inkCenter}, the box at {boxCenter}");

        // Caught the test measuring its own ink detector's false positive:
        // the first version counted the box's own top and bottom border
        // rows — each spanning the full scanned width — as "ink", which
        // stretched the measured range out to the box's own edges and so
        // always averaged back to the box's centre, regardless of where the
        // digits actually sat. That let it pass against a rendering where
        // "13" plainly sat hard against the top edge. Comparing against a
        // plain TextBox measured the same way (same ink detector, same
        // compact height) catches a regression the border artefact would
        // otherwise hide again.
        Assert.True(Math.Abs(referenceOffset - numericOffset) <= 2,
            $"a TextBox's text centres {referenceOffset}px from the top of its box, " +
            $"the NumericUpDown's digit centres {numericOffset}px from the top of its own");
    }

    /// <summary>
    /// Where a control's own ink (its drawn text) centres vertically within
    /// an area, in window coordinates.
    ///
    /// Scanning for "anything that is not the background" also catches the
    /// control's own border: a row of border pixels runs the full width of
    /// the box, so counting it as ink would widen the measured range all
    /// the way to the box's own top and bottom edge, averaging back to the
    /// box's centre regardless of where the digits actually sit. Insetting
    /// by the control's own BorderThickness keeps the border line itself
    /// out of the count. One more row is excluded past that: the
    /// NumericUpDown merges PART_TextBox's own border with the control's
    /// rounded outer border (the inner TextBox sits on a -1 margin so the
    /// two borders draw as one), and that merge anti-aliases a faint extra
    /// pixel of the corner curve one row inside the border — only on the
    /// NumericUpDown, not a plain TextBox, which is exactly the kind of
    /// stray pixel that would quietly bias this measurement again.
    /// </summary>
    private static double InkVerticalCenter(Raster raster, TextBox textBox, Avalonia.PixelRect area)
    {
        var background = textBox.Background is ISolidColorBrush b ? b.Color : Colors.Transparent;
        var topInset = Math.Max(2, (int)Math.Ceiling(textBox.BorderThickness.Top) + 1);
        var bottomInset = Math.Max(2, (int)Math.Ceiling(textBox.BorderThickness.Bottom) + 1);

        var inkRows = new List<int>();

        for (var y = area.Y + topInset; y < area.Bottom - bottomInset; y++)
        {
            for (var x = area.X + 2; x < area.Right - 2; x++)
            {
                if (!Raster.Near(raster.At(x, y), background, tolerance: 20))
                {
                    inkRows.Add(y);
                    break;
                }
            }
        }

        Assert.NotEmpty(inkRows);

        return (inkRows.Min() + inkRows.Max()) / 2.0;
    }

    [AvaloniaFact]
    public void TheSpinnerButtonsStillFitAndCanStillBeClicked()
    {
        var box = new NumericUpDown
        {
            Value = 10,
            Minimum = 0,
            Maximum = 100,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var window = new Window { Content = box, Width = 200, Height = 80 };
        window.Show();
        for (var i = 0; i < 5; i++) window.UpdateLayout();

        var boxBounds = box.Bounds;

        var increase = box.GetVisualDescendants().OfType<RepeatButton>()
            .Single(b => b.Name == "PART_IncreaseButton");
        var decrease = box.GetVisualDescendants().OfType<RepeatButton>()
            .Single(b => b.Name == "PART_DecreaseButton");

        var increaseBounds = Raster.BoundsIn(increase, window);
        var decreaseBounds = Raster.BoundsIn(decrease, window);

        window.Close();

        // Clipped would mean a zero (or negative) size, or a button sitting
        // outside the control it belongs to — either way, nothing a pointer
        // could land on.
        Assert.True(increaseBounds.Height > 0 && increaseBounds.Width > 0);
        Assert.True(decreaseBounds.Height > 0 && decreaseBounds.Width > 0);

        Assert.True(increaseBounds.Bottom <= boxBounds.Bottom + 0.5);
        Assert.True(decreaseBounds.Bottom <= boxBounds.Bottom + 0.5);
    }
}
