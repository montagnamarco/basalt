using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

public sealed class WinFormsDesignerLayoutToolbarTests
{
    [AvaloniaFact]
    public void EightCompactLayoutButtonsPaintBlueGlyphsAndExposeLocalizedNames()
    {
        var toolbar = new WinFormsDesignerLayoutToolbar { SelectedCount = 3 };
        var window = new Window { Content = toolbar, Width = 430, Height = 60 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var buttons = toolbar.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Content is Canvas).ToArray();
            Assert.Equal(8, buttons.Length);
            Assert.All(buttons, button =>
            {
                Assert.True(button.IsEnabled);
                Assert.Equal(32, button.Width);
                var name = AutomationProperties.GetName(button);
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.Equal(name, ToolTip.GetTip(button));
            });
            Assert.Equal(Localizer.Get("WinFormsDesigner_AlignLeft"), AutomationProperties.GetName(buttons[0]));

            using var bitmap = new RenderTargetBitmap(new PixelSize(430, 60));
            bitmap.Render(window);
            var pixels = new byte[430 * 60 * 4];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, 430, 60),
                    handle.AddrOfPinnedObject(), pixels.Length, 430 * 4);
            }
            finally { handle.Free(); }

            foreach (var button in buttons)
            {
                var origin = button.TranslatePoint(new Point(), window)!.Value;
                var bluePixels = 0;
                for (var y = (int)origin.Y; y < Math.Min(60, (int)origin.Y + 28); y++)
                for (var x = (int)origin.X; x < Math.Min(430, (int)origin.X + 32); x++)
                {
                    var index = (y * 430 + x) * 4;
                    if (pixels[index + 2] > pixels[index] + 30) bluePixels++;
                }
                Assert.True(bluePixels > 8, $"Layout icon {AutomationProperties.GetName(button)} did not paint.");
            }
        }
        finally { window.Close(); }
    }
}
