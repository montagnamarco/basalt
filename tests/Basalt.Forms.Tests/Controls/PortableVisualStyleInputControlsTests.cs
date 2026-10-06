using System.Drawing;
using System.Globalization;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Rendering;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Controls;

public class PortableVisualStyleInputControlsTests
{
    [AvaloniaFact]
    public void TabsPaintSelectedAndHotStatesWithoutReplacingOwnerDraw()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var tabs = new TabControl
            {
                Size = new Size(220, 100),
                VisualStylesOverride = true,
                VisualStylePlatformOverride = platform,
                HotTrack = true
            };
            tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));

            var palette = PortableVisualStyles.Palette(platform);
            var selected = tabs.GetTabRect(0);
            var second = tabs.GetTabRect(1);
            using (var normal = Draw(tabs))
            {
                Assert.Equal(palette.Surface.ToArgb(),
                    normal.GetPixel(selected.Left + 3, selected.Top + 3).ToArgb());
                Assert.Equal(palette.Face.ToArgb(),
                    normal.GetPixel(second.Left + 3, second.Top + 3).ToArgb());
            }

            tabs.DefaultMouseMove(new MouseEventArgs(MouseButtons.None, 0,
                second.Left + 4, second.Top + 4, 0));
            using (var hot = Draw(tabs))
            {
                Assert.Equal(palette.HotFace.ToArgb(),
                    hot.GetPixel(second.Left + 3, second.Top + 3).ToArgb());
            }

            tabs.Enabled = false;
            using (var disabled = Draw(tabs))
            {
                Assert.Equal(palette.Face.ToArgb(),
                    disabled.GetPixel(second.Left + 3, second.Top + 3).ToArgb());
            }
            tabs.Enabled = true;

            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            var drawCount = 0;
            tabs.DrawItem += (_, _) => drawCount++;
            using var ownerDrawn = Draw(tabs);
            Assert.Equal(2, drawCount);
        }
    }

    [AvaloniaFact]
    public void ClassicTabsKeepAnExplicitBackgroundAcrossStripAndTab()
    {
        using var tabs = new TabControl
        {
            Size = new Size(220, 100),
            BackColor = Color.Magenta,
            VisualStylesOverride = false
        };
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));
        var second = tabs.GetTabRect(1);

        using var bitmap = Draw(tabs);
        Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(200, 1).ToArgb());
        Assert.Equal(Color.Magenta.ToArgb(),
            bitmap.GetPixel(second.Left + 3, second.Top + 3).ToArgb());
    }

    [AvaloniaFact]
    public void BottomAndVerticalTabsPaintWhereGetTabRectAndMouseHitTestingAgree()
    {
        foreach (var alignment in new[] { TabAlignment.Bottom, TabAlignment.Left, TabAlignment.Right })
        {
            using var tabs = new TabControl
            {
                Size = new Size(220, 140),
                Alignment = alignment,
                VisualStylesOverride = true,
                VisualStylePlatformOverride = PortableVisualStylePlatform.MacOS
            };
            tabs.TabPages.AddRange(new TabPage("First"), new TabPage("Second"));
            var second = tabs.GetTabRect(1);
            var page = tabs.DisplayRectangle;
            Assert.True(alignment switch
            {
                TabAlignment.Bottom => second.Top >= page.Bottom,
                TabAlignment.Left => second.Right <= page.Left,
                _ => second.Left >= page.Right
            });

            var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform.MacOS);
            using (var bitmap = Draw(tabs))
            {
                Assert.Equal(palette.Border.ToArgb(),
                    bitmap.GetPixel(second.Left, second.Top + second.Height / 2).ToArgb());
            }

            tabs.DefaultMouseDown(new MouseEventArgs(MouseButtons.Left, 1,
                second.Left + second.Width / 2, second.Top + second.Height / 2, 0));
            Assert.Equal(1, tabs.SelectedIndex);

            using var selected = Draw(tabs);
            Assert.True(ContainsColorIn(selected, palette.Text,
                Rectangle.Inflate(second, -4, -4)));
        }
    }

    [AvaloniaFact]
    public void GroupBoxUsesProfileBorderOnlyWhenAppearanceIsInherited()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var group = new GroupBox
            {
                Size = new Size(150, 70),
                Text = "Group",
                VisualStylesOverride = true,
                VisualStylePlatformOverride = platform
            };
            var border = PortableVisualStyles.Palette(platform).Border.ToArgb();

            using (var themed = Draw(group))
            {
                Assert.Equal(border, themed.GetPixel(0, 35).ToArgb());
            }

            group.BackColor = Color.Magenta;
            using (var explicitColor = Draw(group))
            {
                Assert.NotEqual(border, explicitColor.GetPixel(0, 35).ToArgb());
            }
        }
    }

    [AvaloniaFact]
    public void BothUpDownControlsPaintProfileArrowsAndNumericPressState()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            var palette = PortableVisualStyles.Palette(platform);
            using var numeric = new TestNumericUpDown
            {
                Size = new Size(120, 30),
                VisualStylesOverride = true,
                VisualStylePlatformOverride = platform
            };
            using var domain = new DomainUpDown
            {
                Size = new Size(120, 30),
                VisualStylesOverride = true,
                VisualStylePlatformOverride = platform
            };

            using (var normal = Draw(numeric))
            using (var domainBitmap = Draw(domain))
            {
                Assert.Equal(palette.Face.ToArgb(), normal.GetPixel(106, 3).ToArgb());
                Assert.Equal(palette.Face.ToArgb(), domainBitmap.GetPixel(106, 3).ToArgb());
            }

            numeric.Press(110, 5);
            using (var pressed = Draw(numeric))
            {
                Assert.Equal(palette.PressedFace.ToArgb(), pressed.GetPixel(106, 3).ToArgb());
            }
            numeric.Release(110, 5);
        }
    }

    [AvaloniaFact]
    public void DateTimePickerPaintsProfileFieldAndPressedSpinnerButHonorsExplicitBackground()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var picker = new DateTimePicker
            {
                Size = new Size(200, 28),
                ShowUpDown = true,
                VisualStylesOverride = true,
                VisualStylePlatformOverride = platform
            };
            var palette = PortableVisualStyles.Palette(platform);

            using (var normal = Draw(picker))
            {
                Assert.Equal(palette.Field.ToArgb(), normal.GetPixel(3, 2).ToArgb());
                Assert.Equal(palette.Face.ToArgb(), normal.GetPixel(185, 4).ToArgb());
            }

            picker.DefaultMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 190, 5, 0));
            using (var pressed = Draw(picker))
            {
                Assert.Equal(palette.PressedFace.ToArgb(), pressed.GetPixel(185, 4).ToArgb());
            }
            picker.DefaultMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 190, 5, 0));

            picker.BackColor = Color.Magenta;
            using var explicitColor = Draw(picker);
            Assert.Equal(Color.Magenta.ToArgb(), explicitColor.GetPixel(3, 2).ToArgb());
        }
    }

    [AvaloniaFact]
    public void TinySpinnerButtonsDoNotDrawOutsideTheirClientBounds()
    {
        using var numeric = new NumericUpDown
        {
            Size = new Size(3, 16),
            VisualStylesOverride = true
        };
        using var picker = new DateTimePicker
        {
            Size = new Size(8, 28),
            ShowUpDown = true,
            VisualStylesOverride = true,
            // The text of a picker is today's date by default: its width changes from day to day
            // and, antialiased, it used to bleed past the client. Pin it so the test is repeatable.
            Value = new DateTime(2024, 6, 15, 13, 45, 30)
        };

        using var numericRaster = Draw(numeric);
        using var pickerRaster = Draw(picker);
        Assert.Equal(numeric.Size, numericRaster.Size);
        Assert.Equal(picker.Size, pickerRaster.Size);

        using var outside = new Bitmap(50, 40);
        using var graphics = Graphics.FromImage(outside);
        graphics.Clear(Color.Magenta);
        graphics.TranslateTransform(20, 0);
        using var paint = new PaintEventArgs(graphics, picker.ClientRectangle);
        picker.DefaultPaint(paint);

        // ButtonBounds begins to the left of a client narrower than ButtonWidth.
        // Rendering without intersection paints the button into this sentinel area.
        Assert.Equal(Color.Magenta.ToArgb(), outside.GetPixel(10, 6).ToArgb());
    }

    [AvaloniaTheory]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 40)]
    [InlineData(true, 40)]
    public void LongDateTextInANarrowPickerStaysInsideTheClient(bool showUpDown, int width)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            using var picker = new DateTimePicker
            {
                Size = new Size(width, 28),
                ShowUpDown = showUpDown,
                VisualStylesOverride = true,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dddd, MMMM dd, yyyy HH:mm:ss",
                Value = new DateTime(2024, 9, 30, 23, 59, 59)
            };

            const int margin = 40;
            using var raster = new Bitmap(width + 2 * margin, 40);
            using var graphics = Graphics.FromImage(raster);
            graphics.Clear(Color.Magenta);
            graphics.TranslateTransform(margin, 0);
            using var paint = new PaintEventArgs(graphics, picker.ClientRectangle);
            picker.DefaultPaint(paint);

            var client = new Rectangle(margin, 0, picker.ClientSize.Width, picker.ClientSize.Height);
            var inside = 0;
            for (var y = 0; y < raster.Height; y++)
            {
                for (var x = 0; x < raster.Width; x++)
                {
                    if (client.Contains(x, y))
                    {
                        if (raster.GetPixel(x, y).ToArgb() != Color.Magenta.ToArgb()) inside++;
                        continue;
                    }

                    Assert.True(raster.GetPixel(x, y).ToArgb() == Color.Magenta.ToArgb(),
                        $"Pixel ({x - margin},{y}) outside the {picker.ClientSize} client was painted.");
                }
            }

            Assert.True(inside > 0, "The picker painted nothing inside its client.");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [AvaloniaFact]
    public void CalendarUsesProfileForTitleAndSelectionWhileKeepingExplicitTitleColor()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var calendar = new MonthCalendar
            {
                VisualStylesOverride = true,
                VisualStylePlatformOverride = platform,
                ShowToday = false,
                ShowTodayCircle = false
            };
            calendar.SetDate(new DateTime(2024, 6, 15));
            var palette = PortableVisualStyles.Palette(platform);

            using (var themed = Draw(calendar))
            {
                Assert.Equal(palette.Face.ToArgb(), themed.GetPixel(4, 4).ToArgb());
                Assert.True(ContainsColor(themed, palette.Selection));
            }

            calendar.TitleBackColor = Color.Magenta;
            using var explicitTitle = Draw(calendar);
            Assert.True(ContainsColor(explicitTitle, Color.Magenta));
        }
    }

    private static bool ContainsColor(Bitmap bitmap, Color color)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == color.ToArgb()) return true;
            }
        }

        return false;
    }

    private static bool ContainsColorIn(Bitmap bitmap, Color color, Rectangle area)
    {
        area.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == color.ToArgb()) return true;
            }
        }

        return false;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }

    private sealed class TestNumericUpDown : NumericUpDown
    {
        internal void Press(int x, int y) => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));

        internal void Release(int x, int y) => OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
    }
}
