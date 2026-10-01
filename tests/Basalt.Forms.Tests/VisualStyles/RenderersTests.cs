using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace Basalt.Forms.Tests.VisualStyles;

/// <summary>
/// The classic "renderer" classes (ButtonRenderer, CheckBoxRenderer...).
/// Basalt never loads a visual style, so every one of them must draw the
/// classic Windows 2000 look through ControlPaint; proved the way AGENTS.md
/// asks for rendering claims, by reading pixels back from a bitmap.
/// </summary>
public class RenderersTests
{
    private static (Bitmap Bitmap, Graphics Graphics) Canvas(int width, int height, Color sentinel)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(sentinel);

        return (bitmap, graphics);
    }

    // Deliberate divergence: on Windows with visual styles off these six throw
    // InvalidOperationException from VisualStyleRenderer. Basalt has no themed
    // look to fall back from, and real applications call them unguarded because
    // Windows always has styles on, so they draw the classic look instead of
    // crashing a ported program.
    [Fact]
    public void Renderers_without_a_classic_path_on_Windows_draw_instead_of_throwing()
    {
        var (bitmap, graphics) = Canvas(40, 40, Color.Magenta);
        using (bitmap)
        using (graphics)
        {
            var bounds = new Rectangle(2, 2, 30, 20);

            ComboBoxRenderer.DrawDropDownButton(graphics, bounds, ComboBoxState.Normal);
            ScrollBarRenderer.DrawArrowButton(graphics, bounds, ScrollBarArrowButtonState.UpNormal);
            TabRenderer.DrawTabItem(graphics, bounds, TabItemState.Normal);
            TextBoxRenderer.DrawTextBox(graphics, bounds, TextBoxState.Normal);
            TrackBarRenderer.DrawHorizontalTrack(graphics, bounds);
            ProgressBarRenderer.DrawHorizontalBar(graphics, bounds);

            Assert.NotEqual(Color.Magenta.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        }
    }

    // --- ButtonRenderer ------------------------------------------------

    [Fact]
    public void ButtonRenderer_IsSupported_does_not_exist_but_RenderMatchingApplicationState_defaults_true()
    {
        Assert.True(ButtonRenderer.RenderMatchingApplicationState);
    }

    [Fact]
    public void ButtonRenderer_draws_a_raised_face_for_Normal_and_a_sunken_one_for_Pressed()
    {
        var (normalBitmap, normalGraphics) = Canvas(30, 20, Color.Magenta);
        using (normalGraphics) ButtonRenderer.DrawButton(normalGraphics, new Rectangle(0, 0, 30, 20), PushButtonState.Normal);

        var (pressedBitmap, pressedGraphics) = Canvas(30, 20, Color.Magenta);
        using (pressedGraphics) ButtonRenderer.DrawButton(pressedGraphics, new Rectangle(0, 0, 30, 20), PushButtonState.Pressed);

        using (normalBitmap)
        using (pressedBitmap)
        {
            // Raised: light-light at the top-left corner. Sunken: dark at it.
            Assert.Equal(SystemColors.ControlLightLight.ToArgb(), normalBitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.ControlDark.ToArgb(), pressedBitmap.GetPixel(0, 0).ToArgb());
        }
    }

    [Fact]
    public void ButtonRenderer_draws_centred_text_in_ControlText_when_enabled()
    {
        var (bitmap, graphics) = Canvas(60, 20, SystemColors.Control);
        using (graphics) ButtonRenderer.DrawButton(graphics, new Rectangle(0, 0, 60, 20), "Hi", Control.DefaultFont, focused: false, PushButtonState.Normal);
        using (bitmap)
        {
            var hasControlText = false;
            for (var x = 2; x < 58 && !hasControlText; x++)
            for (var y = 2; y < 18 && !hasControlText; y++)
                if (bitmap.GetPixel(x, y).ToArgb() == SystemColors.ControlText.ToArgb())
                    hasControlText = true;

            Assert.True(hasControlText, "Expected at least one ControlText pixel from the drawn caption.");
        }
    }

    // --- CheckBoxRenderer ------------------------------------------------

    [Fact]
    public void CheckBoxRenderer_draws_a_check_mark_only_when_checked()
    {
        var (uncheckedBitmap, uncheckedGraphics) = Canvas(16, 16, SystemColors.Window);
        using (uncheckedGraphics) CheckBoxRenderer.DrawCheckBox(uncheckedGraphics, new Point(0, 0), CheckBoxState.UncheckedNormal);

        var (checkedBitmap, checkedGraphics) = Canvas(16, 16, SystemColors.Window);
        using (checkedGraphics) CheckBoxRenderer.DrawCheckBox(checkedGraphics, new Point(0, 0), CheckBoxState.CheckedNormal);

        using (uncheckedBitmap)
        using (checkedBitmap)
        {
            bool HasControlTextPixel(Bitmap bitmap)
            {
                for (var x = 0; x < 13; x++)
                for (var y = 0; y < 13; y++)
                    if (bitmap.GetPixel(x, y).ToArgb() == SystemColors.ControlText.ToArgb())
                        return true;
                return false;
            }

            Assert.False(HasControlTextPixel(uncheckedBitmap));
            Assert.True(HasControlTextPixel(checkedBitmap));
        }
    }

    [Fact]
    public void CheckBoxRenderer_GetGlyphSize_is_the_fixed_classic_thirteen_pixel_box()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);

        Assert.Equal(new Size(13, 13), CheckBoxRenderer.GetGlyphSize(graphics, CheckBoxState.UncheckedNormal));
    }

    // --- RadioButtonRenderer ------------------------------------------------

    [Fact]
    public void RadioButtonRenderer_draws_a_centre_dot_only_when_checked()
    {
        var (uncheckedBitmap, uncheckedGraphics) = Canvas(16, 16, SystemColors.Window);
        using (uncheckedGraphics) RadioButtonRenderer.DrawRadioButton(uncheckedGraphics, new Point(0, 0), RadioButtonState.UncheckedNormal);

        var (checkedBitmap, checkedGraphics) = Canvas(16, 16, SystemColors.Window);
        using (checkedGraphics) RadioButtonRenderer.DrawRadioButton(checkedGraphics, new Point(0, 0), RadioButtonState.CheckedNormal);

        using (uncheckedBitmap)
        using (checkedBitmap)
        {
            Assert.Equal(SystemColors.Window.ToArgb(), uncheckedBitmap.GetPixel(6, 6).ToArgb());
            Assert.Equal(SystemColors.ControlText.ToArgb(), checkedBitmap.GetPixel(6, 6).ToArgb());
        }
    }

    // --- GroupBoxRenderer ------------------------------------------------

    [Fact]
    public void GroupBoxRenderer_draws_an_etched_frame()
    {
        var (bitmap, graphics) = Canvas(40, 30, Color.Magenta);
        using (graphics) GroupBoxRenderer.DrawGroupBox(graphics, new Rectangle(0, 5, 40, 25), GroupBoxState.Normal);
        using (bitmap)
        {
            // Etched draws dark then darker going in from the top-left corner of the frame.
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 5).ToArgb());
        }
    }

    [Fact]
    public void GroupBoxRenderer_erases_the_frame_under_its_caption()
    {
        using var font = new Font("Microsoft Sans Serif", 8.25f);
        var bounds = new Rectangle(0, 0, 100, 40);
        var text = "Caption";
        var textSize = TextRenderer.MeasureText(text, font);
        var textHeight = textSize.Height;

        var (bitmap, graphics) = Canvas(100, 40, Color.Magenta);
        using (graphics) GroupBoxRenderer.DrawGroupBox(graphics, bounds, text, font, GroupBoxState.Normal);
        using (bitmap)
        {
            // The frame's top border falls inside the caption's vertical
            // span; past the end of the drawn text, but still inside the
            // erased strip, it must have been erased to the control face
            // colour instead of showing the border.
            var pastTheText = 8 + textSize.Width + 4;
            Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(pastTheText, textHeight / 2).ToArgb());

            // Outside the caption, to its left, the frame's own left edge survives: it was never erased.
            Assert.NotEqual(SystemColors.Control.ToArgb(), bitmap.GetPixel(0, textHeight + 2).ToArgb());
        }
    }

    // --- TextBoxRenderer ------------------------------------------------

    [Fact]
    public void TextBoxRenderer_draws_a_sunken_frame_around_a_window_coloured_interior()
    {
        var (bitmap, graphics) = Canvas(40, 20, Color.Magenta);
        using (graphics) TextBoxRenderer.DrawTextBox(graphics, new Rectangle(0, 0, 40, 20), TextBoxState.Normal);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.Window.ToArgb(), bitmap.GetPixel(20, 10).ToArgb());
        }
        Assert.False(TextBoxRenderer.IsSupported);
    }

    [Fact]
    public void TextBoxRenderer_fills_Control_coloured_when_disabled_instead_of_Window()
    {
        var (bitmap, graphics) = Canvas(40, 20, Color.Magenta);
        using (graphics) TextBoxRenderer.DrawTextBox(graphics, new Rectangle(0, 0, 40, 20), TextBoxState.Disabled);
        using (bitmap)
        {
            Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(20, 10).ToArgb());
        }
    }

    // --- ComboBoxRenderer ------------------------------------------------

    [Fact]
    public void ComboBoxRenderer_IsSupported_is_false_and_draws_a_drop_down_arrow()
    {
        Assert.False(ComboBoxRenderer.IsSupported);

        var (bitmap, graphics) = Canvas(16, 16, SystemColors.Control);
        using (graphics) ComboBoxRenderer.DrawDropDownButton(graphics, new Rectangle(0, 0, 16, 16), ComboBoxState.Normal);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlText.ToArgb(), bitmap.GetPixel(8, 8).ToArgb());
        }
    }

    // --- ScrollBarRenderer ------------------------------------------------

    [Fact]
    public void ScrollBarRenderer_arrow_button_is_pushed_in_when_Pressed()
    {
        var (normalBitmap, normalGraphics) = Canvas(16, 16, Color.Magenta);
        using (normalGraphics) ScrollBarRenderer.DrawArrowButton(normalGraphics, new Rectangle(0, 0, 16, 16), ScrollBarArrowButtonState.UpNormal);

        var (pressedBitmap, pressedGraphics) = Canvas(16, 16, Color.Magenta);
        using (pressedGraphics) ScrollBarRenderer.DrawArrowButton(pressedGraphics, new Rectangle(0, 0, 16, 16), ScrollBarArrowButtonState.UpPressed);

        using (normalBitmap)
        using (pressedBitmap)
        {
            Assert.Equal(SystemColors.ControlLightLight.ToArgb(), normalBitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.ControlDark.ToArgb(), pressedBitmap.GetPixel(0, 0).ToArgb());
        }

        Assert.False(ScrollBarRenderer.IsSupported);
    }

    // --- TabRenderer ------------------------------------------------

    [Fact]
    public void TabRenderer_leaves_the_bottom_edge_of_a_selected_tab_open()
    {
        var (bitmap, graphics) = Canvas(40, 20, SystemColors.Control);
        using (graphics) TabRenderer.DrawTabItem(graphics, new Rectangle(0, 0, 40, 20), TabItemState.Selected);
        using (bitmap)
        {
            // Top-left corner still has the raised border...
            Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            // ...but the bottom edge was left as the plain face colour, not a border.
            Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(20, 19).ToArgb());
        }
    }

    [Fact]
    public void TabRenderer_draws_a_closed_frame_for_an_unselected_tab()
    {
        var (bitmap, graphics) = Canvas(40, 20, SystemColors.Control);
        using (graphics) TabRenderer.DrawTabItem(graphics, new Rectangle(0, 0, 40, 20), TabItemState.Normal);
        using (bitmap)
        {
            Assert.NotEqual(SystemColors.Control.ToArgb(), bitmap.GetPixel(20, 19).ToArgb());
        }
    }

    // --- TrackBarRenderer ------------------------------------------------

    [Fact]
    public void TrackBarRenderer_draws_a_sunken_groove_through_the_middle_of_the_track()
    {
        var (bitmap, graphics) = Canvas(40, 10, Color.Magenta);
        using (graphics) TrackBarRenderer.DrawHorizontalTrack(graphics, new Rectangle(0, 0, 40, 10));
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 3).ToArgb());
        }
        Assert.False(TrackBarRenderer.IsSupported);
    }

    [Fact]
    public void TrackBarRenderer_thumb_sizes_match_their_orientation()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);

        var top = TrackBarRenderer.GetTopPointingThumbSize(graphics, TrackBarThumbState.Normal);
        var left = TrackBarRenderer.GetLeftPointingThumbSize(graphics, TrackBarThumbState.Normal);

        Assert.True(top.Height > top.Width, "A top-pointing thumb should be taller than it is wide.");
        Assert.True(left.Width > left.Height, "A left-pointing thumb should be wider than it is tall.");
    }

    // --- ProgressBarRenderer ------------------------------------------------

    [Fact]
    public void ProgressBarRenderer_draws_Highlight_chunks_separated_by_gaps()
    {
        var (bitmap, graphics) = Canvas(40, 10, SystemColors.Window);
        using (graphics) ProgressBarRenderer.DrawHorizontalChunks(graphics, new Rectangle(0, 0, 40, 10));
        using (bitmap)
        {
            Assert.Equal(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(0, 5).ToArgb());

            var gapX = ProgressBarRenderer.ChunkThickness + ProgressBarRenderer.ChunkSpaceThickness / 2;
            Assert.Equal(SystemColors.Window.ToArgb(), bitmap.GetPixel(gapX, 5).ToArgb());
        }
        Assert.False(ProgressBarRenderer.IsSupported);
    }
}
