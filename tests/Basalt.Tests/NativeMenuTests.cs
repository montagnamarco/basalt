using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Headless.XUnit;
using Basalt.Shell;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The menu macOS draws at the top of the screen.
///
/// It is not the managed menu: the system draws it, and it takes a bitmap
/// rather than a control. The code used to skip icons here on the belief
/// that they would be ignored — NativeMenuItem has an Icon of its own, so
/// the belief was wrong and the system menu simply had none.
/// </summary>
public class NativeMenuTests
{
    [AvaloniaFact]
    public void TheApplicationIsNamedBasalt()
    {
        // What macOS shows as the first menu, beside the apple. Without it
        // the app menu reads "Avalonia Application".
        Assert.Equal("Basalt", Application.Current?.Name);
    }

    [AvaloniaFact]
    public void AnIconBecomesABitmapForTheSystemMenu()
    {
        var bitmap = IconView.Rasterize(IconKind.Save);

        Assert.NotNull(bitmap);

        // Rendered at twice the logical size, so it stays sharp on a retina
        // screen.
        Assert.True(bitmap.PixelSize.Width >= 32,
            $"the icon is only {bitmap.PixelSize.Width} pixels across");
    }

    [AvaloniaFact]
    public void TheBitmapHasSomethingDrawnOnIt()
    {
        // Size alone proves nothing: without a rasteriser the bitmap comes
        // back the right shape and completely blank, which is exactly what
        // the first version of this test passed over.
        var bitmap = IconView.Rasterize(IconKind.Save, size: 32);

        Assert.NotNull(bitmap);

        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var pixels = new byte[width * height * 4];

        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, width, height),
                handle.AddrOfPinnedObject(),
                pixels.Length,
                width * 4);
        }
        finally
        {
            handle.Free();
        }

        // Some pixel has to be neither transparent nor blank.
        var drawn = 0;

        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] > 0) drawn++;

        Assert.True(drawn > 0, "the rasterised icon is empty");
    }

    [AvaloniaFact]
    public void RasterizingTheSameIconTwiceGivesTheSameBitmap()
    {
        // The menu is rebuilt whenever the language or the theme changes.
        Assert.Same(IconView.Rasterize(IconKind.Build), IconView.Rasterize(IconKind.Build));
    }

    [AvaloniaFact]
    public void NoIconMeansNoBitmap()
    {
        Assert.Null(IconView.Rasterize(IconKind.None));
    }

    [AvaloniaFact]
    public void TheSystemMenuCarriesTheIcons()
    {
        var entries = new List<MenuEntry>
        {
            new("File", Children:
            [
                new("New", Action: () => { }) { Icon = IconKind.New },
                MenuEntry.Separator,
                new("Nameless", Action: () => { })
            ])
        };

        var menu = IdeMenu.BuildNative(entries);

        var file = Assert.IsType<NativeMenuItem>(menu.Items[0]);

        Assert.NotNull(file.Menu);

        var withIcon = Assert.IsType<NativeMenuItem>(file.Menu.Items[0]);
        var withoutIcon = Assert.IsType<NativeMenuItem>(file.Menu.Items[2]);

        Assert.NotNull(withIcon.Icon);

        // And an entry that deliberately has none stays bare.
        Assert.Null(withoutIcon.Icon);
    }
}
