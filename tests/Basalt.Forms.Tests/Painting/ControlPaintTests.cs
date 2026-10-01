using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Painting;

/// <summary>
/// ControlPaint's bevels, glyphs and tints, proved the way AGENTS.md asks
/// for rendering claims: draw into a bitmap and read the pixels back.
/// </summary>
public class ControlPaintTests
{
    private static (Bitmap Bitmap, Graphics Graphics) Canvas(int width, int height, Color sentinel)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(sentinel);

        return (bitmap, graphics);
    }

    // --- Light/Dark/LightLight/DarkDark -----------------------------------

    [Fact]
    public void Dark_and_Light_of_Control_are_the_exact_system_colours()
    {
        Assert.Equal(SystemColors.ControlDark.ToArgb(), ControlPaint.Dark(SystemColors.Control).ToArgb());
        Assert.Equal(SystemColors.ControlDarkDark.ToArgb(), ControlPaint.DarkDark(SystemColors.Control).ToArgb());
        Assert.Equal(SystemColors.ControlLight.ToArgb(), ControlPaint.Light(SystemColors.Control).ToArgb());
        Assert.Equal(SystemColors.ControlLightLight.ToArgb(), ControlPaint.LightLight(SystemColors.Control).ToArgb());
    }

    [Fact]
    public void ContrastControlDark_is_ControlDark()
    {
        Assert.Equal(SystemColors.ControlDark.ToArgb(), ControlPaint.ContrastControlDark.ToArgb());
    }

    [Fact]
    public void Dark_moves_all_the_way_to_black_and_Light_all_the_way_to_white()
    {
        var darkest = ControlPaint.Dark(Color.FromArgb(200, 120, 40), 1.0f);
        var lightest = ControlPaint.Light(Color.FromArgb(200, 120, 40), 1.0f);

        Assert.Equal(Color.Black.ToArgb(), Color.FromArgb(darkest.R, darkest.G, darkest.B).ToArgb());
        Assert.Equal(Color.White.ToArgb(), Color.FromArgb(lightest.R, lightest.G, lightest.B).ToArgb());
    }

    [Fact]
    public void Dark_and_Light_of_a_non_Control_colour_shift_its_brightness()
    {
        var baseColor = Color.FromArgb(180, 90, 90);

        Assert.True(ControlPaint.Dark(baseColor).GetBrightness() < baseColor.GetBrightness());
        Assert.True(ControlPaint.Light(baseColor).GetBrightness() > baseColor.GetBrightness());
    }

    // --- DrawBorder3D -------------------------------------------------------

    [Fact]
    public void DrawBorder3D_Sunken_is_dark_then_darker_at_the_top_left_and_light_then_lighter_at_the_bottom_right()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawBorder3D(graphics, new Rectangle(0, 0, 20, 20), Border3DStyle.Sunken);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.ControlDarkDark.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
            Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(19, 19).ToArgb());
            Assert.Equal(SystemColors.ControlLight.ToArgb(), bitmap.GetPixel(18, 18).ToArgb());
        }
    }

    [Fact]
    public void DrawBorder3D_Raised_is_light_then_lighter_at_the_top_left_and_dark_then_darker_at_the_bottom_right()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawBorder3D(graphics, new Rectangle(0, 0, 20, 20), Border3DStyle.Raised);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.ControlLight.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
            Assert.Equal(SystemColors.ControlDarkDark.ToArgb(), bitmap.GetPixel(19, 19).ToArgb());
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(18, 18).ToArgb());
        }
    }

    [Fact]
    public void DrawBorder3D_Etched_grooves_rather_than_bevels()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawBorder3D(graphics, new Rectangle(0, 0, 20, 20), Border3DStyle.Etched);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.ControlLight.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
            Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(19, 19).ToArgb());
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(18, 18).ToArgb());
        }
    }

    [Fact]
    public void DrawBorder3D_Flat_is_a_single_dark_line()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawBorder3D(graphics, new Rectangle(0, 0, 20, 20), Border3DStyle.Flat);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(19, 19).ToArgb());
            Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
        }
    }

    [Fact]
    public void DrawBorder3D_with_the_Middle_side_fills_the_interior_with_the_control_face()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawBorder3D(graphics, new Rectangle(0, 0, 20, 20), Border3DStyle.Raised, Border3DSide.All);
        using (bitmap) Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
    }

    [Fact]
    public void DrawBorder3D_without_the_Middle_side_leaves_the_interior_untouched()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawBorder3D(graphics, new Rectangle(0, 0, 20, 20));
        using (bitmap) Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
    }

    [Fact]
    public void DrawBorder3D_with_only_the_Top_side_leaves_the_other_sides_untouched()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawBorder3D(graphics, new Rectangle(0, 0, 20, 20), Border3DStyle.Raised, Border3DSide.Top);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(10, 0).ToArgb());
            Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(0, 10).ToArgb());
            Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(19, 10).ToArgb());
        }
    }

    [Fact]
    public void DrawBorder3D_rejects_a_null_Graphics()
    {
        Assert.Throws<ArgumentNullException>(() => ControlPaint.DrawBorder3D(null!, new Rectangle(0, 0, 10, 10)));
    }

    // --- DrawBorder ----------------------------------------------------------

    [Fact]
    public void DrawBorder_Solid_paints_each_side_its_own_colour_and_width()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics)
        {
            ControlPaint.DrawBorder(
                graphics, new Rectangle(0, 0, 20, 20),
                Color.Red, 3, ButtonBorderStyle.Solid,
                Color.Blue, 2, ButtonBorderStyle.Solid,
                Color.Green, 1, ButtonBorderStyle.Solid,
                Color.Yellow, 4, ButtonBorderStyle.Solid);
        }

        using (bitmap)
        {
            Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(10, 0).ToArgb());
            Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(10, 1).ToArgb());
            Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(1, 10).ToArgb());
            Assert.Equal(Color.Green.ToArgb(), bitmap.GetPixel(19, 10).ToArgb());
            Assert.Equal(Color.Yellow.ToArgb(), bitmap.GetPixel(10, 19).ToArgb());
            Assert.Equal(Color.Yellow.ToArgb(), bitmap.GetPixel(10, 16).ToArgb());
        }
    }

    [Fact]
    public void DrawBorder_None_draws_nothing()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawBorder(graphics, new Rectangle(0, 0, 20, 20), Color.Red, ButtonBorderStyle.None);
        using (bitmap) Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void DrawVisualStyleBorder_is_a_one_pixel_ControlDark_rectangle()
    {
        var (bitmap, graphics) = Canvas(10, 10, Color.Magenta);
        using (graphics) ControlPaint.DrawVisualStyleBorder(graphics, new Rectangle(0, 0, 10, 10));
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(9, 0).ToArgb());
            Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(5, 5).ToArgb());
        }
    }

    // --- DrawButton ----------------------------------------------------------

    [Fact]
    public void DrawButton_Normal_is_raised_with_a_Control_face()
    {
        var (bitmap, graphics) = Canvas(30, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawButton(graphics, new Rectangle(0, 0, 30, 20), ButtonState.Normal);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(15, 10).ToArgb());
        }
    }

    [Fact]
    public void DrawButton_Pushed_is_sunken()
    {
        var (bitmap, graphics) = Canvas(30, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawButton(graphics, new Rectangle(0, 0, 30, 20), ButtonState.Pushed);
        using (bitmap) Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void DrawButton_Flat_is_a_single_dark_border()
    {
        var (bitmap, graphics) = Canvas(30, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawButton(graphics, new Rectangle(0, 0, 30, 20), ButtonState.Flat);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(15, 10).ToArgb());
        }
    }

    [Fact]
    public void DrawButton_Inactive_is_a_face_with_no_bevel()
    {
        var (bitmap, graphics) = Canvas(30, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawButton(graphics, new Rectangle(0, 0, 30, 20), ButtonState.Inactive);
        using (bitmap) Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    // --- DrawCheckBox / DrawMixedCheckBox ------------------------------------

    [Fact]
    public void DrawCheckBox_unchecked_has_no_glyph_pixels_in_its_Window_interior()
    {
        var (bitmap, graphics) = Canvas(13, 13, Color.Magenta);
        using (graphics) ControlPaint.DrawCheckBox(graphics, new Rectangle(0, 0, 13, 13), ButtonState.Normal);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.Window.ToArgb(), bitmap.GetPixel(6, 6).ToArgb());
        }
    }

    [Fact]
    public void DrawCheckBox_checked_paints_the_tick_in_ControlText()
    {
        var (bitmap, graphics) = Canvas(13, 13, Color.Magenta);
        using (graphics) ControlPaint.DrawCheckBox(graphics, new Rectangle(0, 0, 13, 13), ButtonState.Checked);

        using (bitmap)
        {
            var interior = Rectangle.FromLTRB(2, 2, 11, 11);
            var hasGlyph = false;

            for (var y = interior.Top; y < interior.Bottom; y++)
            {
                for (var x = interior.Left; x < interior.Right; x++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() == SystemColors.ControlText.ToArgb()) hasGlyph = true;
                }
            }

            Assert.True(hasGlyph);
        }
    }

    [Fact]
    public void DrawCheckBox_Inactive_checked_paints_the_tick_in_GrayText_on_a_Control_interior()
    {
        var (bitmap, graphics) = Canvas(13, 13, Color.Magenta);
        using (graphics) ControlPaint.DrawCheckBox(graphics, new Rectangle(0, 0, 13, 13), ButtonState.Checked | ButtonState.Inactive);

        using (bitmap)
        {
            Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(3, 3).ToArgb());

            var interior = Rectangle.FromLTRB(2, 2, 11, 11);
            var hasGlyph = false;

            for (var y = interior.Top; y < interior.Bottom; y++)
            {
                for (var x = interior.Left; x < interior.Right; x++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() == SystemColors.GrayText.ToArgb()) hasGlyph = true;
                }
            }

            Assert.True(hasGlyph);
        }
    }

    [Fact]
    public void DrawCheckBox_Flat_is_a_one_pixel_border_with_no_sunken_bevel()
    {
        var (bitmap, graphics) = Canvas(13, 13, Color.Magenta);
        using (graphics) ControlPaint.DrawCheckBox(graphics, new Rectangle(0, 0, 13, 13), ButtonState.Flat);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.Window.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
        }
    }

    [Fact]
    public void DrawMixedCheckBox_dithers_its_interior_between_Window_and_Control()
    {
        var (bitmap, graphics) = Canvas(13, 13, Color.Magenta);
        using (graphics) ControlPaint.DrawMixedCheckBox(graphics, new Rectangle(0, 0, 13, 13), ButtonState.Normal);
        using (bitmap)
        {
            Assert.Equal(SystemColors.Window.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
            Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(3, 2).ToArgb());
        }
    }

    // --- DrawRadioButton -------------------------------------------------------

    [Fact]
    public void DrawRadioButton_checked_has_a_centre_dot_in_ControlText()
    {
        var (bitmap, graphics) = Canvas(12, 12, Color.Magenta);
        using (graphics) ControlPaint.DrawRadioButton(graphics, new Rectangle(0, 0, 12, 12), ButtonState.Checked);
        using (bitmap) Assert.Equal(SystemColors.ControlText.ToArgb(), bitmap.GetPixel(6, 6).ToArgb());
    }

    [Fact]
    public void DrawRadioButton_unchecked_has_no_centre_dot()
    {
        var (bitmap, graphics) = Canvas(12, 12, Color.Magenta);
        using (graphics) ControlPaint.DrawRadioButton(graphics, new Rectangle(0, 0, 12, 12), ButtonState.Normal);
        using (bitmap) Assert.Equal(SystemColors.Window.ToArgb(), bitmap.GetPixel(6, 6).ToArgb());
    }

    // --- DrawScrollButton / DrawComboButton -------------------------------------

    [Fact]
    public void DrawScrollButton_Up_fills_a_triangle_pointing_up_in_ControlText()
    {
        var (bitmap, graphics) = Canvas(16, 16, Color.Magenta);
        using (graphics) ControlPaint.DrawScrollButton(graphics, new Rectangle(0, 0, 16, 16), ScrollButton.Up, ButtonState.Normal);
        using (bitmap) Assert.Equal(SystemColors.ControlText.ToArgb(), bitmap.GetPixel(8, 8).ToArgb());
    }

    [Fact]
    public void DrawScrollButton_Pushed_is_sunken_and_shifts_the_glyph()
    {
        var (bitmap, graphics) = Canvas(16, 16, Color.Magenta);
        using (graphics) ControlPaint.DrawScrollButton(graphics, new Rectangle(0, 0, 16, 16), ScrollButton.Up, ButtonState.Pushed);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(SystemColors.ControlText.ToArgb(), bitmap.GetPixel(9, 9).ToArgb());
        }
    }

    [Fact]
    public void DrawComboButton_draws_the_same_down_arrow_as_DrawScrollButton()
    {
        var (bitmap, graphics) = Canvas(16, 16, Color.Magenta);
        using (graphics) ControlPaint.DrawComboButton(graphics, new Rectangle(0, 0, 16, 16), ButtonState.Normal);
        using (bitmap) Assert.Equal(SystemColors.ControlText.ToArgb(), bitmap.GetPixel(8, 8).ToArgb());
    }

    // --- DrawCaptionButton -------------------------------------------------------

    [Fact]
    public void DrawCaptionButton_Close_draws_an_X_through_the_centre()
    {
        var (bitmap, graphics) = Canvas(20, 18, Color.Magenta);
        using (graphics) ControlPaint.DrawCaptionButton(graphics, new Rectangle(0, 0, 20, 18), CaptionButton.Close, ButtonState.Normal);
        using (bitmap) Assert.Equal(SystemColors.ControlText.ToArgb(), bitmap.GetPixel(10, 9).ToArgb());
    }

    [Theory]
    [InlineData(CaptionButton.Close)]
    [InlineData(CaptionButton.Minimize)]
    [InlineData(CaptionButton.Maximize)]
    [InlineData(CaptionButton.Restore)]
    [InlineData(CaptionButton.Help)]
    public void DrawCaptionButton_draws_a_glyph_for_every_kind(CaptionButton button)
    {
        var (bitmap, graphics) = Canvas(20, 18, Color.Magenta);
        using (graphics) ControlPaint.DrawCaptionButton(graphics, new Rectangle(0, 0, 20, 18), button, ButtonState.Normal);

        using (bitmap)
        {
            var changed = false;

            for (var y = 0; y < 18 && !changed; y++)
            {
                for (var x = 0; x < 20; x++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() != SystemColors.Control.ToArgb()) { changed = true; break; }
                }
            }

            Assert.True(changed);
        }
    }

    // --- DrawMenuGlyph -------------------------------------------------------

    [Fact]
    public void DrawMenuGlyph_Bullet_fills_a_dot_in_the_foreground_colour()
    {
        var (bitmap, graphics) = Canvas(16, 16, Color.Magenta);
        using (graphics) ControlPaint.DrawMenuGlyph(graphics, new Rectangle(0, 0, 16, 16), MenuGlyph.Bullet, Color.Red, Color.Blue);
        using (bitmap)
        {
            Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(8, 8).ToArgb());
        }
    }

    [Fact]
    public void DrawMenuGlyph_Arrow_fills_a_triangle_in_the_foreground_colour()
    {
        var (bitmap, graphics) = Canvas(16, 16, Color.Magenta);
        using (graphics) ControlPaint.DrawMenuGlyph(graphics, new Rectangle(0, 0, 16, 16), MenuGlyph.Arrow, Color.Red, Color.Blue);
        using (bitmap) Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(8, 8).ToArgb());
    }

    // --- DrawFocusRectangle -------------------------------------------------------

    [Fact]
    public void DrawFocusRectangle_alternates_pixels_along_its_top_edge()
    {
        var (bitmap, graphics) = Canvas(10, 10, Color.Magenta);
        using (graphics) ControlPaint.DrawFocusRectangle(graphics, new Rectangle(0, 0, 10, 10), Color.Black, Color.White);
        using (bitmap)
        {
            Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
            Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(2, 0).ToArgb());
        }
    }

    // --- Designer adornments -------------------------------------------------------

    [Fact]
    public void DrawGrabHandle_primary_is_black_with_a_white_border()
    {
        var (bitmap, graphics) = Canvas(7, 7, Color.Magenta);
        using (graphics) ControlPaint.DrawGrabHandle(graphics, new Rectangle(0, 0, 7, 7), primary: true, enabled: true);
        using (bitmap)
        {
            Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(3, 3).ToArgb());
        }
    }

    [Fact]
    public void DrawGrabHandle_disabled_falls_back_to_the_control_face()
    {
        var (bitmap, graphics) = Canvas(7, 7, Color.Magenta);
        using (graphics) ControlPaint.DrawGrabHandle(graphics, new Rectangle(0, 0, 7, 7), primary: true, enabled: false);
        using (bitmap) Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void DrawLockedFrame_marches_alternating_pixels_around_the_rectangle()
    {
        var (bitmap, graphics) = Canvas(10, 10, Color.Magenta);
        using (graphics) ControlPaint.DrawLockedFrame(graphics, new Rectangle(0, 0, 10, 10), primary: true);
        using (bitmap)
        {
            Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        }
    }

    [Fact]
    public void DrawSelectionFrame_places_a_handle_at_the_top_left_corner()
    {
        var (bitmap, graphics) = Canvas(30, 30, Color.Magenta);
        using (graphics) ControlPaint.DrawSelectionFrame(graphics, active: true, new Rectangle(0, 0, 30, 30), new Rectangle(5, 5, 20, 20), Color.White);
        using (bitmap) Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void DrawGrid_dots_a_dark_colour_on_a_light_background()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawGrid(graphics, new Rectangle(0, 0, 20, 20), new Size(5, 5), Color.White);
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        }
    }

    // --- Images and disabled text -------------------------------------------------------

    [Fact]
    public void DrawImageDisabled_greys_out_and_lightens_the_image()
    {
        var source = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++) source.SetPixel(x, y, Color.FromArgb(255, 200, 0, 0));

        var (bitmap, graphics) = Canvas(4, 4, Color.Magenta);
        using (graphics) ControlPaint.DrawImageDisabled(graphics, source, 0, 0, Color.White);

        using (bitmap)
        using (source)
        {
            var pixel = bitmap.GetPixel(1, 1);

            Assert.Equal(pixel.R, pixel.G);
            Assert.Equal(pixel.G, pixel.B);
            Assert.True(pixel.R > 0);
        }
    }

    [Fact]
    public void DrawImageDisabled_rejects_null_arguments()
    {
        var (bitmap, graphics) = Canvas(4, 4, Color.Magenta);
        using (graphics)
        using (bitmap)
        {
            Assert.Throws<ArgumentNullException>(() => ControlPaint.DrawImageDisabled(null!, new Bitmap(4, 4), 0, 0, Color.White));
            Assert.Throws<ArgumentNullException>(() => ControlPaint.DrawImageDisabled(graphics, null!, 0, 0, Color.White));
        }
    }

    [Fact]
    public void DrawStringDisabled_Graphics_overload_draws_a_highlight_and_a_shadow_pass()
    {
        var (bitmap, graphics) = Canvas(60, 30, Color.Magenta);
        using var font = new Font("Microsoft Sans Serif", 16f);

        using (graphics)
        {
            ControlPaint.DrawStringDisabled(graphics, "W", font, Color.Black, new RectangleF(0, 0, 60, 30), new StringFormat());
        }

        using (bitmap)
        {
            var sawHighlight = false;
            var sawShadow = false;

            for (var y = 0; y < 30; y++)
            {
                for (var x = 0; x < 60; x++)
                {
                    var argb = bitmap.GetPixel(x, y).ToArgb();
                    if (argb == SystemColors.ControlLightLight.ToArgb()) sawHighlight = true;
                    if (argb == SystemColors.ControlDark.ToArgb()) sawShadow = true;
                }
            }

            Assert.True(sawHighlight);
            Assert.True(sawShadow);
        }
    }

    [Fact]
    public void DrawStringDisabled_IDeviceContext_overload_draws_through_TextRenderer()
    {
        var (bitmap, graphics) = Canvas(60, 30, Color.Magenta);
        using var font = new Font("Microsoft Sans Serif", 16f);

        using (graphics)
        {
            ControlPaint.DrawStringDisabled(graphics, "W", font, Color.Black, new Rectangle(0, 0, 60, 30), TextFormatFlags.Default);
        }

        using (bitmap)
        {
            var sawShadow = false;

            for (var y = 0; y < 30 && !sawShadow; y++)
            {
                for (var x = 0; x < 60; x++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() == SystemColors.ControlDark.ToArgb()) { sawShadow = true; break; }
                }
            }

            Assert.True(sawShadow);
        }
    }

    [Fact]
    public void DrawSizeGrip_draws_a_highlight_and_shadow_dot_pair_from_the_corner()
    {
        var (bitmap, graphics) = Canvas(20, 20, Color.Magenta);
        using (graphics) ControlPaint.DrawSizeGrip(graphics, Color.White, new Rectangle(0, 0, 20, 20));
        using (bitmap)
        {
            Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(16, 16).ToArgb());
            Assert.Equal(SystemColors.ControlDarkDark.ToArgb(), bitmap.GetPixel(14, 14).ToArgb());
        }
    }

    // --- Members that need native handles, and the screen-only no-ops -------------

    [Fact]
    public void CreateHBitmap_members_say_there_are_no_native_handles()
    {
        Assert.Throws<PlatformNotSupportedException>(() => ControlPaint.CreateHBitmap16Bit(new Bitmap(2, 2), Color.White));
        Assert.Throws<PlatformNotSupportedException>(() => ControlPaint.CreateHBitmapColorMask(new Bitmap(2, 2), IntPtr.Zero));
        Assert.Throws<PlatformNotSupportedException>(() => ControlPaint.CreateHBitmapTransparencyMask(new Bitmap(2, 2)));
    }

    [Fact]
    public void Reversible_screen_XOR_members_do_nothing_but_do_not_throw()
    {
        ControlPaint.DrawReversibleFrame(new Rectangle(0, 0, 10, 10), Color.Black, FrameStyle.Dashed);
        ControlPaint.DrawReversibleLine(new Point(0, 0), new Point(10, 10), Color.Black);
        ControlPaint.FillReversibleRectangle(new Rectangle(0, 0, 10, 10), Color.Black);
    }

    [Fact]
    public void DrawButton_rejects_a_null_Graphics()
    {
        Assert.Throws<ArgumentNullException>(() => ControlPaint.DrawButton(null!, new Rectangle(0, 0, 10, 10), ButtonState.Normal));
    }
}
