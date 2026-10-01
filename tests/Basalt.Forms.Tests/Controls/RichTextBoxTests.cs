using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>
/// The formatted document (Selection* properties), the RTF reader/writer
/// round trip, LoadFile/SaveFile, formatted layout and painting, Find, and
/// undo/redo.
/// </summary>
public class RichTextBoxTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 10f);

    private static Form Show(RichTextBox box)
    {
        box.Font = Font;
        box.Bounds = new Rectangle(10, 10, 300, 150);
        var form = new Form { ClientSize = new Size(400, 200) };
        form.Controls.Add(box);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();

        return form;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    /// <summary>
    /// The x of the rightmost painted (non-background) pixel on the row that holds the text's ink, within
    /// [xStart, xEnd): the box's Fixed3D frame paints its own non-background pixels at the very edges, which
    /// would otherwise always win as "rightmost" regardless of how wide the text itself is.
    /// </summary>
    private static int RightmostInkX(Bitmap bitmap, int y, Color background, int xStart, int xEnd)
    {
        var rightmost = -1;
        for (var x = xStart; x < xEnd; x++)
        {
            if (bitmap.GetPixel(x, y).ToArgb() != background.ToArgb()) rightmost = x;
        }

        return rightmost;
    }

    // --- Selection* properties: the common value, or the default when the selection is mixed ------------------

    [Fact]
    public void SelectionFont_reads_bold_only_when_the_whole_selection_is_bold()
    {
        var box = new RichTextBox { Font = Font, Text = "Hello World" };

        box.Select(0, 5);
        box.SelectionFont = new Font(Font, FontStyle.Bold);
        Assert.True(box.SelectionFont!.Bold);

        box.Select(0, 11);
        Assert.Null(box.SelectionFont);

        box.Select(0, 5);
        Assert.True(box.SelectionFont!.Bold);
    }

    [Fact]
    public void SelectionColor_and_SelectionBackColor_apply_to_the_selection_and_read_back_mixed_as_empty()
    {
        var box = new RichTextBox { Font = Font, Text = "Red Green" };

        box.Select(0, 3);
        box.SelectionColor = Color.Red;
        box.Select(4, 5);
        box.SelectionColor = Color.Green;
        box.Select(4, 5);
        box.SelectionBackColor = Color.Yellow;

        box.Select(0, 3);
        Assert.Equal(Color.Red.ToArgb(), box.SelectionColor.ToArgb());

        box.Select(4, 5);
        Assert.Equal(Color.Green.ToArgb(), box.SelectionColor.ToArgb());
        Assert.Equal(Color.Yellow.ToArgb(), box.SelectionBackColor.ToArgb());

        box.Select(0, 9);
        Assert.Equal(Color.Empty, box.SelectionColor);
    }

    [Fact]
    public void A_format_set_at_an_empty_selection_is_what_the_next_typed_character_takes()
    {
        var box = new RichTextBox { Font = Font, Text = "AB" };

        box.Select(1, 0);
        box.SelectionColor = Color.Blue;
        box.SelectedText = "X";

        box.Select(1, 1);
        Assert.Equal(Color.Blue.ToArgb(), box.SelectionColor.ToArgb());

        box.Select(0, 1);
        Assert.NotEqual(Color.Blue.ToArgb(), box.SelectionColor.ToArgb());
    }

    [Fact]
    public void Paragraph_properties_apply_to_whichever_paragraph_the_selection_touches()
    {
        var box = new RichTextBox { Font = Font, Text = "one\r\ntwo\r\nthree" };

        box.Select(6, 0);
        box.SelectionAlignment = HorizontalAlignment.Center;
        box.SelectionIndent = 20;
        box.SelectionBullet = true;

        box.Select(6, 0);
        Assert.Equal(HorizontalAlignment.Center, box.SelectionAlignment);
        Assert.Equal(20, box.SelectionIndent);
        Assert.True(box.SelectionBullet);

        box.Select(0, 0);
        Assert.Equal(HorizontalAlignment.Left, box.SelectionAlignment);
        Assert.False(box.SelectionBullet);
    }

    [Fact]
    public void SelectionProtected_blocks_an_edit_and_raises_Protected()
    {
        var box = new RichTextBox { Font = Font, Text = "locked free" };
        box.Select(0, 6);
        box.SelectionProtected = true;

        var raised = 0;
        box.Protected += (_, _) => raised++;

        box.Select(0, 6);
        box.SelectedText = "XXXXXX";

        Assert.Equal("locked free", box.Text);
        Assert.Equal(1, raised);

        box.Select(7, 4);
        box.SelectedText = "open";
        Assert.Equal("locked open", box.Text);
    }

    // --- Rtf and SelectedRtf: a round trip through our own reader and writer -----------------------------------

    [Fact]
    public void Rtf_round_trips_bold_colour_alignment_and_bullet()
    {
        var box = new RichTextBox { Font = Font, Text = "Bold Plain" };
        box.Select(0, 4);
        box.SelectionFont = new Font(Font, FontStyle.Bold);
        box.SelectionColor = Color.Red;
        box.Select(0, 10);
        box.SelectionAlignment = HorizontalAlignment.Center;
        box.SelectionBullet = true;

        var rtf = box.Rtf;
        Assert.Contains("\\rtf1", rtf);
        Assert.Contains("\\b1", rtf);
        Assert.Contains("\\qc", rtf);
        Assert.Contains("\\pntext", rtf);

        var reloaded = new RichTextBox();
        reloaded.Rtf = rtf;

        Assert.Equal("Bold Plain", reloaded.Text);
        reloaded.Select(0, 4);
        Assert.True(reloaded.SelectionFont!.Bold);
        Assert.Equal(Color.Red.ToArgb(), reloaded.SelectionColor.ToArgb());
        reloaded.Select(0, 10);
        Assert.Equal(HorizontalAlignment.Center, reloaded.SelectionAlignment);
        Assert.True(reloaded.SelectionBullet);
    }

    [Fact]
    public void Rtf_round_trips_a_non_ascii_character_through_its_u_escape()
    {
        var box = new RichTextBox { Font = Font, Text = "café éè" };

        var rtf = box.Rtf;
        Assert.Contains("\\u", rtf);

        var reloaded = new RichTextBox { Rtf = rtf };
        Assert.Equal("café éè", reloaded.Text);
    }

    [Fact]
    public void SelectedRtf_carries_only_the_selected_runs_formatting()
    {
        var box = new RichTextBox { Font = Font, Text = "aaa two three" };
        box.Select(4, 3);
        box.SelectionFont = new Font(Font, FontStyle.Italic);

        var fragment = box.SelectedRtf;
        Assert.Contains("\\i1", fragment);
        Assert.DoesNotContain("aaa", fragment);

        var target = new RichTextBox { Font = Font, Text = "XXX" };
        target.Select(0, 3);
        target.SelectedRtf = fragment;

        Assert.Equal("two", target.Text);
        target.Select(0, 3);
        Assert.True(target.SelectionFont!.Italic);
    }

    // --- LoadFile / SaveFile: RichText, PlainText and UnicodePlainText, by path and by stream -------------------

    [Fact]
    public void SaveFile_and_LoadFile_round_trip_rich_text_through_a_path()
    {
        var path = Path.Combine(Path.GetTempPath(), $"basalt-richtextbox-{Guid.NewGuid():N}.rtf");
        try
        {
            var box = new RichTextBox { Font = Font, Text = "Saved" };
            box.Select(0, 5);
            box.SelectionFont = new Font(Font, FontStyle.Bold);
            box.SaveFile(path);

            var loaded = new RichTextBox();
            loaded.LoadFile(path);

            Assert.Equal("Saved", loaded.Text);
            loaded.Select(0, 5);
            Assert.True(loaded.SelectionFont!.Bold);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveFile_and_LoadFile_round_trip_unicode_plain_text_through_a_stream()
    {
        using var stream = new MemoryStream();
        var box = new RichTextBox { Font = Font, Text = "Plain é text" };
        box.SaveFile(stream, RichTextBoxStreamType.UnicodePlainText);

        stream.Position = 0;
        var loaded = new RichTextBox();
        loaded.LoadFile(stream, RichTextBoxStreamType.UnicodePlainText);

        Assert.Equal("Plain é text", loaded.Text);
    }

    // --- Layout and painting: pixel evidence for the claims that depend on formatted runs ----------------------

    [AvaloniaFact]
    public void Bold_text_paints_wider_than_the_same_plain_text()
    {
        var plainBox = new RichTextBox { Font = Font, Text = "Wmi", Multiline = false };
        var form1 = Show(plainBox);
        var plainBitmap = Draw(plainBox);
        var plainWidth = RightmostInkX(plainBitmap, plainBox.Font.Height / 2, SystemColors.Window, 2, 150);
        form1.Close();

        var boldBox = new RichTextBox { Font = Font, Text = "Wmi", Multiline = false };
        boldBox.Select(0, 3);
        var form2 = Show(boldBox);
        boldBox.SelectionFont = new Font(Font, FontStyle.Bold);
        var boldBitmap = Draw(boldBox);
        var boldWidth = RightmostInkX(boldBitmap, boldBox.Font.Height / 2, SystemColors.Window, 2, 150);
        form2.Close();

        Assert.True(plainWidth > 0, "Expected the plain text to paint some ink.");
        Assert.True(boldWidth > plainWidth, $"Expected bold ({boldWidth}) to paint wider than plain ({plainWidth}).");
    }

    /// <summary>A pixel whose dominant channel is <paramref name="channel"/>, clear of anti-aliased blending with the white background.</summary>
    private static bool IsDominant(Color pixel, int channel)
    {
        var channels = new[] { pixel.R, pixel.G, pixel.B };
        return channels[channel] > 120 && channels[channel] - channels.Where((_, i) => i != channel).Max() > 60;
    }

    [AvaloniaFact]
    public void A_coloured_run_paints_in_its_own_colour()
    {
        var box = new RichTextBox { Font = Font, Text = "RedBlue", Multiline = false };
        box.Select(0, 3);
        box.SelectionColor = Color.Red;
        box.Select(3, 4);
        box.SelectionColor = Color.Blue;
        var form = Show(box);

        var bitmap = Draw(box);
        var sawRed = false;
        var sawBlue = false;

        for (var y = 1; y < box.Font.Height + 2; y++)
        {
            for (var x = 2; x < 150; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (IsDominant(pixel, channel: 0)) sawRed = true;
                if (IsDominant(pixel, channel: 2)) sawBlue = true;
            }
        }

        form.Close();

        Assert.True(sawRed, "Expected to find a pixel dominated by the red run's colour.");
        Assert.True(sawBlue, "Expected to find a pixel dominated by the blue run's colour.");
    }

    [AvaloniaFact]
    public void A_centred_paragraph_starts_further_right_than_a_left_aligned_one()
    {
        var box = new RichTextBox { Font = Font, Text = "Hi", Multiline = false };
        var form = Show(box);

        var leftBitmap = Draw(box);
        var leftStart = LeftmostInkX(leftBitmap, box.Font.Height / 2, SystemColors.Window, 2, box.Width - 2);

        box.SelectionAlignment = HorizontalAlignment.Center;
        var centredBitmap = Draw(box);
        var centredStart = LeftmostInkX(centredBitmap, box.Font.Height / 2, SystemColors.Window, 2, box.Width - 2);

        form.Close();

        Assert.True(centredStart > leftStart, $"Expected the centred paragraph ({centredStart}) to start further right than the left-aligned one ({leftStart}).");
    }

    private static int LeftmostInkX(Bitmap bitmap, int y, Color background, int xStart, int xEnd)
    {
        for (var x = xStart; x < xEnd; x++)
        {
            if (bitmap.GetPixel(x, y).ToArgb() != background.ToArgb()) return x;
        }

        return -1;
    }

    // --- Find ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Find_locates_a_substring_and_selects_it()
    {
        var box = new RichTextBox { Text = "one two three two" };

        var first = box.Find("two");
        Assert.Equal(4, first);
        Assert.Equal("two", box.SelectedText);

        var last = box.Find("two", RichTextBoxFinds.Reverse);
        Assert.Equal(14, last);

        Assert.Equal(-1, box.Find("missing"));
    }

    [Fact]
    public void Find_with_WholeWord_skips_a_match_inside_a_longer_word()
    {
        var box = new RichTextBox { Text = "cat category cat" };

        var whole = box.Find("cat", 0, -1, RichTextBoxFinds.WholeWord);
        Assert.Equal(0, whole);

        box.Select(whole + 3, 0);
        var next = box.Find("cat", whole + 3, -1, RichTextBoxFinds.WholeWord);
        Assert.Equal(13, next);
    }

    [Fact]
    public void Find_char_array_locates_the_first_matching_character()
    {
        var box = new RichTextBox { Text = "abc,def" };

        var index = box.Find([',', ';']);

        Assert.Equal(3, index);
    }

    // --- Undo and redo: a stack of document snapshots, simple but correct for formatting-only edits too ---------

    [Fact]
    public void Undo_reverses_a_formatting_only_change_and_Redo_reapplies_it()
    {
        var box = new RichTextBox { Font = Font, Text = "Hello" };
        box.Select(0, 5);
        box.SelectionFont = new Font(Font, FontStyle.Bold);

        Assert.True(box.CanUndo);
        box.Undo();

        box.Select(0, 5);
        Assert.False(box.SelectionFont!.Bold);
        Assert.True(box.CanRedo);

        box.Redo();
        box.Select(0, 5);
        Assert.True(box.SelectionFont!.Bold);
    }

    // --- DetectUrls and LinkClicked ---------------------------------------------------------------------------

    [AvaloniaFact]
    public void Clicking_a_detected_url_raises_LinkClicked()
    {
        var box = new RichTextBox { Font = Font, Text = "see http://example.com for more", DetectUrls = true };
        var form = Show(box);

        LinkClickedEventArgs? clicked = null;
        box.LinkClicked += (_, e) => clicked = e;

        var linkStart = box.Text.IndexOf("http://", StringComparison.Ordinal);
        var point = box.GetPositionFromCharIndex(linkStart + 2);
        form.Host!.MouseDown(new Avalonia.Point(box.Left + point.X, box.Top + point.Y + 2), Avalonia.Input.MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(box.Left + point.X, box.Top + point.Y + 2), Avalonia.Input.MouseButton.Left);

        form.Close();

        Assert.NotNull(clicked);
        Assert.Equal("http://example.com", clicked!.LinkText);
    }

    // --- ZoomFactor: it scales measurement and painting, never the stored format ---------------------------------

    [Fact]
    public void ZoomFactor_scales_the_painted_text_without_changing_SelectionFont()
    {
        // Font before Text: the document's default character format is captured from the ambient Font in
        // effect when each run of text is set, same as TextBoxBase: setting Font does not retroactively
        // reformat text already in the box, only the box's own default for what it does not know yet.
        var box = new RichTextBox { Font = Font, Text = "Zoom" };
        var before = box.GetPositionFromCharIndex(3);

        box.ZoomFactor = 2f;
        var after = box.GetPositionFromCharIndex(3);

        box.Select(0, 4);
        Assert.Equal(Font.SizeInPoints, box.SelectionFont!.SizeInPoints);
        Assert.True(after.X > before.X, $"Expected a doubled zoom to widen the laid-out text ({before.X} -> {after.X}).");
    }

    // --- Hit testing against the formatted layout -----------------------------------------------------------------

    [AvaloniaFact]
    public void GetLineFromCharIndex_and_GetCharIndexFromPosition_agree_with_the_formatted_layout()
    {
        var box = new RichTextBox { Font = Font, Text = "first line\r\nsecond line" };
        var form = Show(box);

        Assert.Equal(0, box.GetLineFromCharIndex(0));
        Assert.Equal(1, box.GetLineFromCharIndex(box.Text.IndexOf("second")));

        var position = box.GetPositionFromCharIndex(0);
        var index = box.GetCharIndexFromPosition(new Point(position.X + 1, position.Y + 1));
        Assert.Equal(0, index);

        form.Close();
    }

    [Theory]
    [InlineData(@"{\rtf1 abc\'")]
    [InlineData(@"{\rtf1 abc\'zz def}")]
    [InlineData(@"{\rtf1 abc\'4")]
    public void A_truncated_or_malformed_hex_escape_never_throws(string rtf)
    {
        var box = new RichTextBox { Rtf = rtf };

        Assert.StartsWith("abc", box.Text);
    }

    [Theory]
    [InlineData(@"{\rtf1\uc0 x\u233 y}", "xéy")]
    [InlineData(@"{\rtf1\uc1 x\u233?y}", "xéy")]
    [InlineData(@"{\rtf1\uc2 x\u233\'e9?y}", "xéy")]
    [InlineData(@"{\rtf1\uc0 {\uc2 a\u233??}x\u233 y}", "aéxéy")]
    public void Unicode_escapes_skip_exactly_the_fallback_ucN_announces(string rtf, string expected)
    {
        var box = new RichTextBox { Rtf = rtf };

        Assert.Equal(expected, box.Text.TrimEnd('\n', '\r'));
    }

    [Fact]
    public void An_unrelated_ignorable_group_does_not_clear_a_bullet()
    {
        var box = new RichTextBox { Rtf = @"{\rtf1{\pntext\'B7\tab}{\*\pn\pnlvlblt}one{\*\company Acme}\par}" };

        box.Select(0, 0);

        Assert.True(box.SelectionBullet);
    }

    [AvaloniaFact]
    public void SelectionChanged_is_raised_once_per_real_change_from_code_and_by_Undo()
    {
        var box = new RichTextBox { Font = Font, Text = "hello world" };
        var form = Show(box);
        var changes = 0;
        box.SelectionChanged += (_, _) => changes++;

        box.SelectAll();
        Assert.Equal(1, changes);

        box.SelectAll();
        Assert.Equal(1, changes);

        box.SelectionStart = 3;
        Assert.Equal(2, changes);

        box.SelectedText = "XYZ";
        var afterEdit = changes;
        box.Undo();
        Assert.True(changes > afterEdit);

        form.Close();
    }

    [AvaloniaFact]
    public void Line_starts_agree_with_the_formatted_lines()
    {
        var box = new RichTextBox { Font = Font, Multiline = true, WordWrap = true, Width = 160, Text = "aaaa bbbb cccc dddd eeee ffff gggg" };
        var form = Show(box);
        box.Width = 160; // Show sets 300: narrow it so the large run has to wrap.
        box.Select(0, 14);
        box.SelectionFont = new Font(Font.FontFamily, 24f);

        // The large run wraps into more lines than the same text in one small font would.
        var lastLine = box.GetLineFromCharIndex(box.TextLength);
        Assert.True(lastLine >= 3, $"expected the 24-point run to wrap, got {lastLine + 1} lines");

        for (var line = 0; line <= lastLine; line++)
        {
            var start = box.GetFirstCharIndexFromLine(line);

            Assert.True(start >= 0, $"line {line} has no start");
            Assert.Equal(line, box.GetLineFromCharIndex(start));
        }

        Assert.Equal(-1, box.GetFirstCharIndexFromLine(lastLine + 1));

        // A forward selection ends at the caret: the current line is the caret's, not the selection start's.
        box.Select(0, box.TextLength);
        Assert.Equal(box.GetFirstCharIndexFromLine(lastLine), box.GetFirstCharIndexOfCurrentLine());

        form.Close();
    }
}
