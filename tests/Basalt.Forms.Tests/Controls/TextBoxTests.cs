using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>The edit control's rules: typing, selection, the clipboard, undo, and its size.</summary>
public class TextBoxTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static (Form Form, TextBox Box) Show(TextBox box)
    {
        box.Font = Font;
        box.Bounds = new Rectangle(10, 10, 150, box.Height);
        var form = new Form { ClientSize = new Size(200, 120) };
        form.Controls.Add(box);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();

        return (form, box);
    }

    private static void Type(Form form, string text)
    {
        foreach (var character in text) form.Host!.KeyTextInput(character.ToString());
    }

    private static void Press(Form form, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        form.Host!.KeyPressQwerty(key, modifiers);
        form.Host.KeyReleaseQwerty(key, modifiers);
    }

    [Fact]
    public void A_single_line_box_is_one_font_line_plus_the_frame_high()
    {
        var box = new TextBox { Font = Font };

        Assert.Equal(Font.Height + 7, box.PreferredHeight);
        Assert.Equal(box.PreferredHeight, box.Height);

        box.Height = 100;
        Assert.Equal(box.PreferredHeight, box.Height);

        box.Multiline = true;
        box.Height = 100;
        Assert.Equal(100, box.Height);
    }

    [AvaloniaFact]
    public void Typing_inserts_at_the_caret_and_backspace_deletes_before_it()
    {
        var (form, box) = Show(new TextBox());

        Type(form, "Helo");
        Press(form, PhysicalKey.ArrowLeft);
        Type(form, "l");
        Assert.Equal("Hello", box.Text);
        Assert.Equal(4, box.SelectionStart);

        Press(form, PhysicalKey.End);
        Press(form, PhysicalKey.Backspace);
        Assert.Equal("Hell", box.Text);
        Assert.True(box.Modified);

        form.Close();
    }

    [AvaloniaFact]
    public void Shift_and_control_arrows_select_by_character_and_by_word()
    {
        var (form, box) = Show(new TextBox { Text = "one two three" });

        Press(form, PhysicalKey.Home);
        Press(form, PhysicalKey.ArrowRight, RawInputModifiers.Control);
        Assert.Equal(4, box.SelectionStart);

        Press(form, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal("two ", box.SelectedText);

        Type(form, "2 ");
        Assert.Equal("one 2 three", box.Text);

        form.Close();
    }

    [AvaloniaFact]
    public void The_KeyPress_handler_can_refuse_a_character()
    {
        var box = new TextBox();
        box.KeyPress += (_, e) => e.Handled = !char.IsDigit(e.KeyChar);
        var (form, _) = Show(box);

        Type(form, "a1b2");

        Assert.Equal("12", box.Text);

        form.Close();
    }

    [AvaloniaFact]
    public void Enter_in_a_multiline_box_makes_a_line_only_when_it_accepts_returns()
    {
        var (form, box) = Show(new TextBox { Multiline = true });
        var accept = new Button();
        form.Controls.Add(accept);
        form.AcceptButton = accept;
        var accepted = 0;
        accept.Click += (_, _) => accepted++;
        box.Focus();

        Type(form, "a");
        Press(form, PhysicalKey.Enter);
        Assert.Equal("a", box.Text);
        Assert.Equal(1, accepted);

        box.AcceptsReturn = true;
        Press(form, PhysicalKey.Enter);
        Type(form, "b");
        Assert.Equal("a\r\nb", box.Text);
        Assert.Equal(["a", "b"], box.Lines);

        form.Close();
    }

    [Fact]
    public void Undo_restores_the_text_and_a_second_undo_redoes()
    {
        var box = new TextBox { Text = "abc" };
        box.Select(3, 0);
        box.Paste("def");

        box.Undo();
        Assert.Equal("abc", box.Text);

        box.Undo();
        Assert.Equal("abcdef", box.Text);
    }

    [Fact]
    public void Setting_the_text_clears_the_undo_the_modified_flag_and_the_selection()
    {
        var box = new TextBox();
        box.Paste("x");
        Assert.True(box.Modified);

        box.Text = "new";

        Assert.False(box.CanUndo);
        Assert.False(box.Modified);
        Assert.Equal(0, box.SelectionStart);
    }

    [Fact]
    public void MaxLength_limits_what_is_pasted_but_not_what_is_set()
    {
        var box = new TextBox { MaxLength = 5 };

        box.Paste("abcdefgh");
        Assert.Equal("abcde", box.Text);

        box.Text = "longer than five";
        Assert.Equal("longer than five", box.Text);
    }

    [Fact]
    public void A_single_line_box_keeps_only_the_first_line_of_pasted_text()
    {
        var box = new TextBox();

        box.Paste("first\r\nsecond");

        Assert.Equal("first", box.Text);
    }

    [Fact]
    public void Character_casing_applies_to_typed_text()
    {
        var box = new TextBox { CharacterCasing = CharacterCasing.Upper };

        box.Paste("mixed Case");

        Assert.Equal("MIXED CASE", box.Text);
    }

    [AvaloniaFact]
    public void Copy_and_paste_go_through_the_clipboard()
    {
        var (form, box) = Show(new TextBox { Text = "copy me" });

        box.SelectAll();
        Press(form, PhysicalKey.C, RawInputModifiers.Control);
        Press(form, PhysicalKey.End);
        Press(form, PhysicalKey.V, RawInputModifiers.Control);

        Assert.Equal("copy mecopy me", box.Text);
        Assert.Equal("copy me", Clipboard.GetText());

        form.Close();
    }

    [Fact]
    public void Character_positions_and_indexes_agree()
    {
        var box = new TextBox { Font = Font, Text = "Hello world", Width = 200 };

        var position = box.GetPositionFromCharIndex(6);
        var index = box.GetCharIndexFromPosition(new Point(position.X + 1, position.Y + 2));

        Assert.Equal(6, index);
        Assert.True(position.X > box.GetPositionFromCharIndex(5).X);
    }

    [Fact]
    public void The_text_is_drawn_on_the_window_colour_inside_a_sunken_edge()
    {
        var box = new TextBox { Font = Font, Text = "Hi", Width = 60 };
        using var bitmap = new Bitmap(box.Width, box.Height);
        box.DrawToBitmap(bitmap, new Rectangle(0, 0, box.Width, box.Height));

        Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(SystemColors.Window.ToArgb(), bitmap.GetPixel(box.Width - 5, box.Height / 2).ToArgb());

        var dark = 0;
        for (var x = 2; x < 20; x++)
        {
            for (var y = 2; y < box.Height - 2; y++)
            {
                if (bitmap.GetPixel(x, y).R < 100) dark++;
            }
        }

        Assert.True(dark > 5, "The text was not drawn.");
    }

    [Fact]
    public void Summed_character_advances_match_the_width_of_the_drawn_line()
    {
        const string text = "The quick brown fox jumps over the lazy dog, 0123456789!";
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        format.HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None;

        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        var whole = graphics.MeasureString(text, Font, new PointF(0, 0), format).Width;
        var summed = text.Sum(character => graphics.MeasureString(character.ToString(), Font, new PointF(0, 0), format).Width);

        // The caret is placed by the sum; a drift would put it beside the glyphs.
        Assert.InRange(summed - whole, -0.5f, 0.5f);
    }

    [Fact]
    public void A_lone_carriage_return_breaks_the_line_as_Lines_does()
    {
        var box = new TextBox { Multiline = true, Text = "one\rtwo\nthree" };

        Assert.Equal(["one", "two", "three"], box.Lines);
        Assert.Equal(1, box.GetLineFromCharIndex(4));
        Assert.Equal(2, box.GetLineFromCharIndex(8));
    }

    [Fact]
    public void A_wrapped_line_ends_where_the_next_begins_without_a_break_character()
    {
        var box = new TextBox { Font = Font, Multiline = true, WordWrap = true, Size = new Size(70, 60), Text = "aaaa bbbb cccc dddd eeee" };

        var secondLine = box.GetFirstCharIndexFromLine(1);

        Assert.True(secondLine > 0, "The text should wrap.");
        Assert.Equal(0, box.GetLineFromCharIndex(secondLine - 1));
        Assert.Equal(1, box.GetLineFromCharIndex(secondLine));
    }
}
