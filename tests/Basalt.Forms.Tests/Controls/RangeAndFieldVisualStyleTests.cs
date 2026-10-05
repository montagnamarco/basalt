using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Rendering;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Controls;

public sealed class RangeAndFieldVisualStyleTests
{
    [AvaloniaFact]
    public void ProfilesSupplyFormSurfacesAndKeepExplicitAppearance()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = CreateForm(platform);
            using var label = new Label { Bounds = new Rectangle(10, 10, 100, 25) };
            form.Controls.Add(label);
            var palette = PortableVisualStyles.Palette(platform);
            using var raster = Draw(label);
            Assert.Equal(palette.Surface.ToArgb(), raster.GetPixel(50, 12).ToArgb());
            Assert.Equal(palette.Text, label.ForeColor);
            Assert.Equal(palette.FontSize, label.Font.SizeInPoints);
            Assert.False(form.ShouldSerializeBackColor());
            Assert.False(form.ShouldSerializeForeColor());

            form.BackColor = Color.Magenta;
            form.ForeColor = Color.DarkRed;
            using var explicitRaster = Draw(label);
            Assert.Equal(Color.Magenta.ToArgb(), explicitRaster.GetPixel(50, 12).ToArgb());
            Assert.Equal(Color.DarkRed, label.ForeColor);
        }
    }

    [AvaloniaFact]
    public void TextFieldsUseProfileBordersAndDisabledSurfacesWithoutChangingBounds()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = CreateForm(platform);
            using var field = new TextBox { Size = new Size(130, 28), AutoSize = false };
            form.Controls.Add(field);
            var bounds = field.Bounds;
            var palette = PortableVisualStyles.Palette(platform);
            using var enabled = Draw(field);
            Assert.Equal(palette.Field.ToArgb(), enabled.GetPixel(60, 10).ToArgb());
            Assert.Equal(palette.Border.ToArgb(), enabled.GetPixel(0, 10).ToArgb());
            field.Enabled = false;
            using var disabled = Draw(field);
            Assert.Equal(palette.Surface.ToArgb(), disabled.GetPixel(60, 10).ToArgb());
            field.BackColor = Color.LightPink;
            using var custom = Draw(field);
            Assert.Equal(Color.LightPink.ToArgb(), custom.GetPixel(60, 10).ToArgb());
            Assert.Equal(bounds, field.Bounds);
        }
    }

    [AvaloniaFact]
    public void ProgressProfilesPaintFillEmptyAndDisabledStatesAndRespectCustomColors()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = CreateForm(platform);
            using var progress = new ProgressBar { Size = new Size(140, 22), Value = 50 };
            form.Controls.Add(progress);
            var palette = PortableVisualStyles.Palette(platform);
            using var normal = Draw(progress);
            Assert.Equal(palette.Face.ToArgb(), normal.GetPixel(120, 10).ToArgb());
            Assert.Equal(palette.Border.ToArgb(), normal.GetPixel(0, 10).ToArgb());
            Assert.NotEqual(palette.Face.ToArgb(), normal.GetPixel(30, 10).ToArgb());
            progress.Enabled = false;
            using var disabled = Draw(progress);
            Assert.Equal(palette.DisabledText.ToArgb(), disabled.GetPixel(30, 10).ToArgb());
            progress.Enabled = true;
            progress.ForeColor = Color.Magenta;
            progress.BackColor = Color.LightPink;
            using var custom = Draw(progress);
            Assert.Equal(Color.Magenta.ToArgb(), custom.GetPixel(30, 10).ToArgb());
            Assert.Equal(Color.LightPink.ToArgb(), custom.GetPixel(120, 10).ToArgb());
        }
    }

    [AvaloniaFact]
    public void SliderAndScrollBarsUseProfilePaintingWithoutChangingGeometry()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = CreateForm(platform);
            using var slider = new TrackBar { Size = new Size(160, 45), Value = 5, TickStyle = TickStyle.None };
            using var scrollbar = new HScrollBar { Size = new Size(160, 18), Value = 20 };
            form.Controls.Add(slider);
            form.Controls.Add(scrollbar);
            var sliderBounds = slider.Bounds;
            var scrollbarBounds = scrollbar.Bounds;
            using var sliderRaster = Draw(slider);
            using var scrollRaster = Draw(scrollbar);
            var palette = PortableVisualStyles.Palette(platform);
            Assert.True(HasColor(sliderRaster, palette.Accent));
            Assert.True(HasColor(scrollRaster, palette.Border));
            Assert.Equal(sliderBounds, slider.Bounds);
            Assert.Equal(scrollbarBounds, scrollbar.Bounds);
        }
    }

    [AvaloniaFact]
    public void VerticalSliderFillRunsFromMinimumAtTheBottomToMaximumAtTheTop()
    {
        using var form = CreateForm(PortableVisualStylePlatform.MacOS);
        using var slider = new TrackBar
        {
            Size = new Size(45, 160),
            Orientation = Orientation.Vertical,
            TickStyle = TickStyle.None
        };
        form.Controls.Add(slider);
        var accent = PortableVisualStyles.Palette(PortableVisualStylePlatform.MacOS).Accent;
        using var minimum = Draw(slider);
        Assert.False(HasColor(minimum, accent));
        slider.Value = slider.Maximum;
        using var maximum = Draw(slider);
        Assert.True(HasColor(maximum, accent));
    }

    [AvaloniaFact]
    public void ChangingPreviewProfileInvalidatesDescendantPicturesAndFonts()
    {
        using var form = CreateForm(PortableVisualStylePlatform.MacOS);
        using var progress = new ProgressBar { Size = new Size(140, 22), Value = 50 };
        using var label = new Label { Size = new Size(100, 25) };
        form.Controls.Add(progress);
        form.Controls.Add(label);
        using var before = Draw(progress);
        Assert.Equal(PortableVisualStyles.Palette(PortableVisualStylePlatform.MacOS).Accent.ToArgb(),
            before.GetPixel(30, 10).ToArgb());

        form.VisualStylePlatformOverride = PortableVisualStylePlatform.Linux;

        using var after = Draw(progress);
        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform.Linux);
        Assert.Equal(palette.Accent.ToArgb(), after.GetPixel(30, 10).ToArgb());
        Assert.Equal(palette.FontSize, label.Font.SizeInPoints);
        Assert.Equal(palette.Text, label.ForeColor);
    }

    [AvaloniaFact]
    public void LinksUseProfileAccentAndDisabledTextAndKeepExplicitLinkColor()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = CreateForm(platform);
            using var link = new LinkLabel { Text = "Open project", Size = new Size(160, 30) };
            form.Controls.Add(link);
            var palette = PortableVisualStyles.Palette(platform);
            using var normal = Draw(link);
            Assert.True(HasColor(normal, palette.Accent));
            link.Enabled = false;
            using var disabled = Draw(link);
            Assert.True(HasColor(disabled, palette.DisabledText));
            link.Enabled = true;
            link.LinkColor = Color.Magenta;
            using var custom = Draw(link);
            Assert.True(HasColor(custom, Color.Magenta));
        }
    }

    private static Form CreateForm(PortableVisualStylePlatform platform) => new()
    {
        VisualStylesOverride = true,
        VisualStylePlatformOverride = platform,
        FormBorderStyle = FormBorderStyle.None
    };

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }

    private static bool HasColor(Bitmap bitmap, Color color) => Enumerable.Range(0, bitmap.Width).Any(x =>
        Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).ToArgb() == color.ToArgb()));
}
