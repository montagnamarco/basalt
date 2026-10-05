using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Rendering;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Controls;

public class PortableVisualStyleTests
{
    [AvaloniaFact]
    public void PlatformProfilesPaintActualButtonsAndSupplyInheritedFonts()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = new Form
            {
                VisualStylesOverride = true,
                VisualStylePlatformOverride = platform
            };
            using var button = new Button { Bounds = new Rectangle(10, 10, 90, 30), TabStop = false };
            form.Controls.Add(button);
            using var bitmap = Draw(button);
            var palette = PortableVisualStyles.Palette(platform);
            if (platform == PortableVisualStylePlatform.Windows)
            {
                Assert.True(bitmap.GetPixel(45, 4).R > bitmap.GetPixel(45, 25).R);
            }
            else
            {
                Assert.Equal(palette.Face.ToArgb(), bitmap.GetPixel(45, 15).ToArgb());
            }

            if (platform == PortableVisualStylePlatform.MacOS)
            {
                Assert.NotEqual(palette.Border.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            }
            Assert.Equal(palette.Border.ToArgb(), bitmap.GetPixel(0, 15).ToArgb());
            Assert.Equal(palette.FontSize, button.Font.SizeInPoints);
            Assert.Same(button.Font, button.Font);
            Assert.True(button.Font.IsSystemFont);
            Assert.False(button.ShouldSerializeFont());
            Assert.False(button.ShouldSerializeBackColor());
        }
    }

    [AvaloniaFact]
    public void ExplicitAppearanceAndClassicFlatControlsRemainUnchanged()
    {
        using var font = new Font("Tahoma", 12);
        using var form = new Form { VisualStylesOverride = true };
        using var button = new Button
        {
            Bounds = new Rectangle(10, 10, 90, 30),
            BackColor = Color.Magenta,
            Font = font,
            TabStop = false
        };
        form.Controls.Add(button);
        using var bitmap = Draw(button);
        Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(45, 15).ToArgb());
        Assert.Same(font, button.Font);

        button.ResetBackColor();
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.Red;
        using var flat = Draw(button);
        Assert.Equal(Color.Red.ToArgb(), flat.GetPixel(0, 15).ToArgb());

        button.FlatStyle = FlatStyle.Standard;
        form.BackColor = Color.Yellow;
        using var inherited = Draw(button);
        Assert.Equal(Color.Yellow.ToArgb(), inherited.GetPixel(45, 15).ToArgb());
    }

    [AvaloniaFact]
    public void ScopedPreviewStylesDoNotMutateApplicationSettings()
    {
        var before = Application.UseVisualStyles;
        using var preview = new Form { VisualStylesOverride = true };
        using var button = new Button();
        preview.Controls.Add(button);
        Assert.True(button.PortableVisualStylesEnabled);
        Assert.Equal(before, Application.UseVisualStyles);
    }

    [AvaloniaFact]
    public void ExplicitVisualStyleOptOutPaintsClassicUntilReset()
    {
        using var button = new Button
        {
            VisualStylesOverride = true,
            VisualStylePlatformOverride = PortableVisualStylePlatform.Linux,
            Size = new Size(90, 30),
            TabStop = false
        };
        Assert.True(button.UseVisualStyleBackColor);
        button.UseVisualStyleBackColor = false;
        using var classic = Draw(button);
        Assert.Equal(SystemColors.Control.ToArgb(), classic.GetPixel(45, 15).ToArgb());

        var descriptor = System.ComponentModel.TypeDescriptor.GetProperties(button)[nameof(Button.UseVisualStyleBackColor)]!;
        Assert.True(descriptor.CanResetValue(button));
        descriptor.ResetValue(button);
        Assert.True(button.UseVisualStyleBackColor);
        using var themed = Draw(button);
        Assert.Equal(PortableVisualStyles.Palette(PortableVisualStylePlatform.Linux).Face.ToArgb(),
            themed.GetPixel(45, 15).ToArgb());
    }

    [AvaloniaFact]
    public void CheckboxAndRadioGlyphsUseThePlatformAccent()
    {
        foreach (var radio in new[] { false, true })
        {
            using var control = radio
                ? (Control)new RadioButton { Checked = true }
                : new CheckBox { Checked = true };
            control.VisualStylesOverride = true;
            control.VisualStylePlatformOverride = PortableVisualStylePlatform.Windows;
            control.Size = new Size(80, 24);
            using var bitmap = Draw(control);
            var accent = PortableVisualStyles.Palette(PortableVisualStylePlatform.Windows).Accent.ToArgb();
            Assert.True(Enumerable.Range(0, bitmap.Width).Any(x =>
                Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).ToArgb() == accent)));
        }
    }

    [AvaloniaFact]
    public void TextBoxFrameUsesPlatformBorderWithoutChangingItsLogicalSize()
    {
        using var textbox = new TextBox
        {
            VisualStylesOverride = true,
            VisualStylePlatformOverride = PortableVisualStylePlatform.Linux,
            Size = new Size(120, 30)
        };
        var size = textbox.Size;
        using var bitmap = Draw(textbox);
        Assert.Equal(PortableVisualStyles.Palette(PortableVisualStylePlatform.Linux).Border.ToArgb(),
            bitmap.GetPixel(0, bitmap.Height / 2).ToArgb());
        Assert.Equal(size, textbox.Size);
        textbox.BackColor = Color.Yellow;
        using var explicitColor = Draw(textbox);
        Assert.NotEqual(PortableVisualStyles.Palette(PortableVisualStylePlatform.Linux).Border.ToArgb(),
            explicitColor.GetPixel(0, explicitColor.Height / 2).ToArgb());
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }
}
