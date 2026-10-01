using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Interop;
using System.Drawing.Text;
using System.Globalization;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Fonts and text: the metrics GDI+ reports, how a string is laid out, and that
/// drawing, measuring and outlining agree with each other.
/// </summary>
public class TextTests
{
    /// <summary>
    /// The numbers Microsoft documents for Arial, in design units. Liberation
    /// Sans, which stands in for Arial where it is missing, was drawn to the
    /// same metrics.
    /// </summary>
    [Fact]
    public void Arial_reports_the_design_metrics_GDI_plus_documents()
    {
        using var arial = new FontFamily("Arial");

        Assert.Equal(2048, arial.GetEmHeight(FontStyle.Regular));
        Assert.Equal(1854, arial.GetCellAscent(FontStyle.Regular));
        Assert.Equal(434, arial.GetCellDescent(FontStyle.Regular));
        Assert.Equal(2355, arial.GetLineSpacing(FontStyle.Regular));
    }

    [Fact]
    public void A_font_s_height_is_its_line_spacing_at_its_size()
    {
        using var font = new Font("Arial", 10f);

        // 10 points at 96 dpi is 13 1/3 pixels of em; the line is 2355/2048 of that.
        var expected = 2355f / 2048f * (10f * 96f / 72f);

        Assert.Equal(expected, font.GetHeight(), 3);
        Assert.Equal((int)MathF.Ceiling(expected), font.Height);
        Assert.Equal(10f, font.SizeInPoints);
    }

    [Fact]
    public void A_font_in_pixels_converts_to_points_at_the_screen_resolution()
    {
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);

        Assert.Equal(9f, font.SizeInPoints, 4);
    }

    [Fact]
    public void An_unknown_family_falls_back_and_remembers_the_name_asked_for()
    {
        using var font = new Font("No Such Typeface Anywhere", 9f);

        Assert.Equal("Microsoft Sans Serif", font.Name);
        Assert.Equal("No Such Typeface Anywhere", font.OriginalFontName);
        Assert.Throws<ArgumentException>(() => new FontFamily("No Such Typeface Anywhere"));
    }

    [Fact]
    public void Windows_font_names_are_kept_even_when_a_stand_in_draws_them()
    {
        using var font = new Font("Microsoft Sans Serif", 8.25f);
        using var segoe = new FontFamily("Segoe UI");

        Assert.Equal("Microsoft Sans Serif", font.Name);
        Assert.Equal("Segoe UI", segoe.Name);
        Assert.Equal("Microsoft Sans Serif", FontFamily.GenericSansSerif.Name);
        Assert.Equal("Courier New", FontFamily.GenericMonospace.Name);
    }

    [Fact]
    public void A_font_s_size_must_be_positive_and_its_unit_not_the_display()
    {
        Assert.Throws<ArgumentException>(() => new Font("Arial", 0f));
        Assert.Throws<ArgumentException>(() => new Font("Arial", float.NaN));
        Assert.Throws<ArgumentException>(() => new Font("Arial", 9f, GraphicsUnit.Display));
    }

    [Fact]
    public void The_font_converter_reads_and_writes_the_text_designers_store()
    {
        var converter = new FontConverter();
        var culture = CultureInfo.InvariantCulture;

        using var font = (Font)converter.ConvertFrom(null, culture, "Arial, 8.25pt, style=Bold, Italic")!;

        Assert.Equal("Arial", font.Name);
        Assert.Equal(8.25f, font.Size);
        Assert.Equal(FontStyle.Bold | FontStyle.Italic, font.Style);
        Assert.Equal("Arial, 8.25pt, style=Bold, Italic", converter.ConvertTo(null, culture, font, typeof(string)));

        using var pixels = (Font)converter.ConvertFrom(null, culture, "Arial, 12px")!;
        Assert.Equal(GraphicsUnit.Pixel, pixels.Unit);
    }

    [Fact]
    public void A_LOGFONT_carries_the_face_size_and_style_there_and_back()
    {
        using var font = new Font("Arial", 10f, FontStyle.Bold | FontStyle.Underline);

        font.ToLogFont(out LOGFONT logFont);

        Assert.Equal(-13, logFont.lfHeight);
        Assert.Equal(700, logFont.lfWeight);
        Assert.Equal(1, logFont.lfUnderline);
        Assert.Equal("Arial", new string(logFont.lfFaceName).TrimEnd('\0'));

        using var back = Font.FromLogFont(logFont);

        Assert.Equal("Arial", back.Name);
        Assert.Equal(FontStyle.Bold | FontStyle.Underline, back.Style);
        Assert.Equal(13f, back.Size);
    }

    /// <summary>
    /// GenericDefault leaves a sixth of an em at each end of the line, which
    /// GenericTypographic does not: the two widths differ by a third of an em.
    /// </summary>
    [Fact]
    public void The_default_format_adds_a_sixth_of_an_em_at_each_end()
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);

        var typographic = graphics.MeasureString("Hello world", font, new PointF(0, 0), StringFormat.GenericTypographic);
        var standard = graphics.MeasureString("Hello world", font);

        Assert.Equal(typographic.Width + 12f / 3f, standard.Width, 3);
        Assert.Equal(font.GetHeight(graphics), standard.Height, 3);
    }

    [Fact]
    public void Text_wider_than_its_rectangle_wraps_at_spaces()
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);
        var word = graphics.MeasureString("wwwww", font, new PointF(0, 0), StringFormat.GenericTypographic).Width;

        var size = graphics.MeasureString("wwwww wwwww wwwww", font, new SizeF(word * 1.5f, 0), StringFormat.GenericTypographic, out var characters, out var lines);

        Assert.Equal(3, lines);
        Assert.Equal(17, characters);
        Assert.Equal(3 * font.GetHeight(graphics), size.Height, 3);
        Assert.True(size.Width <= word * 1.5f);
    }

    /// <summary>
    /// When a line is a word and a half wide, the second word starts the next
    /// line whole, at its left edge, instead of being cut after its first
    /// letters.
    /// </summary>
    [Fact]
    public void A_wrapped_word_starts_the_next_line_whole()
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);
        var word = graphics.MeasureString("wwwww", font, new PointF(0, 0), StringFormat.GenericTypographic).Width;
        using var format = new StringFormat(StringFormat.GenericTypographic);
        format.SetMeasurableCharacterRanges([new CharacterRange(6, 5)]);

        var second = graphics.MeasureCharacterRanges("wwwww wwwww", font, new RectangleF(0, 0, word * 1.5f, 100), format)[0].GetBounds(graphics);

        Assert.Equal(0f, second.Left, 2);
        Assert.Equal(font.GetHeight(graphics), second.Top, 2);
        Assert.Equal(word, second.Width, 2);
    }

    [Fact]
    public void A_word_longer_than_the_line_breaks_inside_itself()
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);
        var half = graphics.MeasureString("wwwww", font, new PointF(0, 0), StringFormat.GenericTypographic).Width;

        graphics.MeasureString("wwwwwwwwww", font, new SizeF(half + 1, 0), StringFormat.GenericTypographic, out _, out var lines);

        Assert.Equal(2, lines);
    }

    [Fact]
    public void LineLimit_keeps_only_lines_that_fit_whole()
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);
        var height = font.GetHeight(graphics) * 1.5f;

        graphics.MeasureString("one\ntwo\nthree", font, new SizeF(200, height), new StringFormat(StringFormatFlags.LineLimit), out _, out var whole);
        graphics.MeasureString("one\ntwo\nthree", font, new SizeF(200, height), new StringFormat(), out _, out var started);

        Assert.Equal(1, whole);
        Assert.Equal(2, started);
    }

    /// <summary>
    /// The lines cut off below the rectangle are dropped, not joined to the
    /// last line that shows: "one" stays "one", and only its three characters
    /// count as fitted.
    /// </summary>
    [Fact]
    public void Lines_cut_off_below_are_not_joined_to_the_last_one_shown()
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormatFlags.LineLimit);
        format.SetMeasurableCharacterRanges([new CharacterRange(8, 5)]);
        var area = new SizeF(200, font.GetHeight(graphics) * 1.5f);

        var size = graphics.MeasureString("one\ntwo\nthree", font, area, format, out var characters, out _);
        var one = graphics.MeasureString("one", font, new PointF(), format);
        var three = graphics.MeasureCharacterRanges("one\ntwo\nthree", font, new RectangleF(PointF.Empty, area), format)[0];

        Assert.Equal(3, characters);
        Assert.Equal(one.Width, size.Width, 2);
        Assert.True(three.IsEmpty(graphics));
    }

    [Fact]
    public void A_LOGFONT_with_a_positive_height_gives_the_cell_not_the_em()
    {
        var logFont = new LOGFONT { lfHeight = 2288, lfWeight = 400 };
        "Arial".AsSpan().CopyTo(logFont.lfFaceName);

        using var font = Font.FromLogFont(logFont);

        // Arial's cell is 1854 + 434 = 2288 units of a 2048 em.
        Assert.Equal(2048f, font.Size, 1);
    }

    [Fact]
    public void An_ampersand_hotkey_takes_no_room_when_hidden_or_shown()
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);
        using var hide = new StringFormat { HotkeyPrefix = HotkeyPrefix.Hide };
        using var none = new StringFormat();

        Assert.Equal(graphics.MeasureString("File", font, new PointF(), none).Width, graphics.MeasureString("&File", font, new PointF(), hide).Width, 3);
        Assert.Equal(graphics.MeasureString("A&B", font, new PointF(), none).Width, graphics.MeasureString("A&&B", font, new PointF(), hide).Width, 3);
    }

    [Fact]
    public void Drawn_text_lies_in_its_line_and_its_rectangle()
    {
        using var bitmap = new Bitmap(120, 40);
        using var font = new Font("Arial", 16f, GraphicsUnit.Pixel);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.DrawString("Hello", font, Brushes.Black, new RectangleF(10, 10, 100, 20));
        }

        var (left, top, right, bottom) = InkBounds(bitmap);

        Assert.True(left >= 10 && top >= 10 && right < 110 && bottom < 30, $"Ink at {left},{top}-{right},{bottom}.");
        Assert.True(right - left > 20, "The text drew almost nothing.");
    }

    [Fact]
    public void Far_alignment_puts_the_text_against_the_right_of_its_rectangle()
    {
        using var near = new Bitmap(200, 30);
        using var far = new Bitmap(200, 30);
        using var font = new Font("Arial", 16f, GraphicsUnit.Pixel);
        using var right = new StringFormat { Alignment = StringAlignment.Far };

        using (var graphics = Graphics.FromImage(near)) graphics.DrawString("Hi", font, Brushes.Black, new RectangleF(0, 0, 200, 30));
        using (var graphics = Graphics.FromImage(far)) graphics.DrawString("Hi", font, Brushes.Black, new RectangleF(0, 0, 200, 30), right);

        Assert.True(InkBounds(near).Right < 40);
        Assert.True(InkBounds(far).Left > 160);
    }

    [Fact]
    public void Text_outside_its_rectangle_is_clipped_unless_NoClip_says_otherwise()
    {
        using var clipped = new Bitmap(200, 40);
        using var free = new Bitmap(200, 40);
        using var font = new Font("Arial", 16f, GraphicsUnit.Pixel);
        using var noClip = new StringFormat(StringFormatFlags.NoClip | StringFormatFlags.NoWrap) { Trimming = StringTrimming.None };
        using var noWrap = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.None };

        using (var graphics = Graphics.FromImage(clipped)) graphics.DrawString("Wide text here", font, Brushes.Black, new RectangleF(0, 0, 40, 30), noWrap);
        using (var graphics = Graphics.FromImage(free)) graphics.DrawString("Wide text here", font, Brushes.Black, new RectangleF(0, 0, 40, 30), noClip);

        Assert.True(InkBounds(clipped).Right < 40);
        Assert.True(InkBounds(free).Right > 60);
    }

    [Fact]
    public void An_ellipsis_ends_text_trimmed_to_its_rectangle()
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);
        using var ellipsis = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };

        graphics.MeasureString("A long sentence that does not fit", font, new SizeF(60, 20), ellipsis, out var characters, out var lines);

        Assert.Equal(1, lines);
        Assert.True(characters < 20, $"{characters} characters said to fit in 60 pixels.");
    }

    [Fact]
    public void Character_ranges_cover_the_characters_they_name()
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 12f, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic);
        format.SetMeasurableCharacterRanges([new CharacterRange(6, 5)]);

        var regions = graphics.MeasureCharacterRanges("Hello world", font, new RectangleF(0, 0, 500, 50), format);
        var bounds = regions[0].GetBounds(graphics);
        // The space after "Hello" counts here: the range starts after it.
        using var withSpaces = new StringFormat(StringFormat.GenericTypographic);
        withSpaces.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        var hello = graphics.MeasureString("Hello ", font, new PointF(), withSpaces).Width;
        var world = graphics.MeasureString("world", font, new PointF(), StringFormat.GenericTypographic).Width;

        Assert.Equal(hello, bounds.Left, 2);
        Assert.Equal(world, bounds.Width, 2);
    }

    [Fact]
    public void A_string_in_a_path_lies_where_DrawString_would_put_it()
    {
        using var path = new GraphicsPath();
        using var family = new FontFamily("Arial");

        path.AddString("Hi", family, (int)FontStyle.Regular, 20f, new PointF(10, 10), StringFormat.GenericTypographic);

        var bounds = path.GetBounds();
        Assert.True(path.PointCount > 0);
        Assert.True(bounds.Left >= 10 && bounds.Top >= 10 && bounds.Bottom <= 10 + 20 * 2355f / 2048f, $"Outline at {bounds}.");
    }

    [Fact]
    public void System_fonts_are_marked_with_their_names()
    {
        using var caption = SystemFonts.CaptionFont;

        Assert.True(caption.IsSystemFont);
        Assert.Equal("CaptionFont", caption.SystemFontName);
        Assert.True(caption.Bold);
        Assert.Null(SystemFonts.GetFontByName("NoSuchFont"));
    }

    [Fact]
    public void The_generic_formats_are_new_each_time()
    {
        var first = StringFormat.GenericDefault;
        first.Alignment = StringAlignment.Far;

        Assert.Equal(StringAlignment.Near, StringFormat.GenericDefault.Alignment);
        Assert.Equal(StringTrimming.None, StringFormat.GenericTypographic.Trimming);
    }

    [Fact]
    public void A_private_font_file_that_does_not_exist_is_refused()
    {
        using var collection = new PrivateFontCollection();

        Assert.Throws<FileNotFoundException>(() => collection.AddFontFile("/no/such/font.ttf"));
        Assert.Empty(collection.Families);
    }

    private static (int Left, int Top, int Right, int Bottom) InkBounds(Bitmap bitmap)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0) continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return (left, top, right, bottom);
    }
}
