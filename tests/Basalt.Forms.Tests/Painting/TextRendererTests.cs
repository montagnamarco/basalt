using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Painting;

/// <summary>GDI's text measuring rules, which controls size themselves by.</summary>
public class TextRendererTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    [Fact]
    public void Empty_text_is_one_line_high_and_nothing_wide()
    {
        Assert.Equal(new Size(0, Font.Height), TextRenderer.MeasureText(string.Empty, Font));
    }

    [Fact]
    public void A_line_is_as_wide_as_its_glyphs_plus_the_overhang_margins()
    {
        var typographic = MeasureTypographic("Hello");
        var overhang = Font.Height / 6;

        var measured = TextRenderer.MeasureText("Hello", Font);

        Assert.Equal((int)Math.Ceiling(typographic) + 2 * overhang, measured.Width);
        Assert.Equal(Font.Height, measured.Height);
    }

    [Fact]
    public void NoPadding_and_LeftAndRightPadding_change_only_the_margins()
    {
        var plain = TextRenderer.MeasureText("Hello", Font, Size.Empty, TextFormatFlags.NoPadding);
        var padded = TextRenderer.MeasureText("Hello", Font);
        var wide = TextRenderer.MeasureText("Hello", Font, Size.Empty, TextFormatFlags.LeftAndRightPadding);
        var overhang = Font.Height / 6;

        Assert.Equal(plain.Width + 2 * overhang, padded.Width);
        Assert.Equal(plain.Width + 4 * overhang, wide.Width);
    }

    [Fact]
    public void Word_break_wraps_into_lines_of_the_font_height()
    {
        var single = TextRenderer.MeasureText("one two three four", Font);
        var wrapped = TextRenderer.MeasureText("one two three four", Font, new Size(single.Width / 2, 0), TextFormatFlags.WordBreak);

        Assert.True(wrapped.Height >= 2 * Font.Height, $"Expected at least two lines, got {wrapped}.");
        Assert.Equal(0, wrapped.Height % Font.Height);
    }

    [Fact]
    public void The_ampersand_is_a_prefix_unless_NoPrefix()
    {
        var withPrefix = TextRenderer.MeasureText("&File", Font);
        var withoutPrefix = TextRenderer.MeasureText("&File", Font, Size.Empty, TextFormatFlags.NoPrefix);

        Assert.True(withoutPrefix.Width > withPrefix.Width);
    }

    [Fact]
    public void Text_is_drawn_inside_its_rectangle_in_the_fore_colour()
    {
        using var bitmap = new Bitmap(80, 20, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            TextRenderer.DrawText(graphics, "Hamburg", Font, new Rectangle(0, 0, 80, 20), Color.Black);
        }

        var dark = 0;
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 80; x++)
            {
                if (bitmap.GetPixel(x, y).R < 128) dark++;
            }
        }

        Assert.True(dark > 20, $"Only {dark} dark pixels: the text was not drawn.");
    }

    private static float MeasureTypographic(string text)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        format.HotkeyPrefix = HotkeyPrefix.Show;

        return graphics.MeasureString(text, Font, new SizeF(0, 0), format).Width;
    }
}
