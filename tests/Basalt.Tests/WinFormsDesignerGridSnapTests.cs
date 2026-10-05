using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

public sealed class WinFormsDesignerGridSnapTests
{
    [AvaloniaFact]
    public void GridDotsRenderAtSameLogicalLocationsAt96_144And192Dpi()
    {
        var (window, surface, form, _) = Open();
        try
        {
            foreach (var dpi in new[] { 96, 144, 192 })
            {
                var dot = BrightnessNear(window, dpi, 28, 28);
                var blank = BrightnessNear(window, dpi, 32, 28);
                Assert.True(dot + 30 < blank,
                    $"grid dot should be visible at {dpi} dpi: dot={dot}, blank={blank}");

                surface.ShowGrid = false;
                var hidden = BrightnessNear(window, dpi, 28, 28);
                Assert.True(hidden > dot + 30,
                    $"hiding the grid should remove its raster dot at {dpi} dpi");
                surface.ShowGrid = true;
            }
        }
        finally { window.Close(); form.Dispose(); }
    }

    [AvaloniaFact]
    public void DragUsesGridWithAltOverrideAndPrefersNearbyControlGuides()
    {
        var (window, surface, form, panel) = Open();
        try
        {
            surface.SnapToGrid = true;
            surface.SnapToControls = false;
            var at = OnForm(70, 55);
            window.MouseDown(at, MouseButton.Left);
            window.MouseMove(at + new Vector(10, 10));
            window.MouseUp(at + new Vector(10, 10), MouseButton.Left);
            Assert.Equal(new Drawing.Point(48, 40), panel.Location);

            at = OnForm(80, 65);
            window.MouseDown(at, MouseButton.Left);
            window.MouseMove(at + new Vector(5, 3), RawInputModifiers.Alt);
            window.MouseUp(at + new Vector(5, 3), MouseButton.Left);
            Assert.Equal(new Drawing.Point(53, 43), panel.Location);

            panel.Location = new Drawing.Point(35, 30);
            var sibling = new Forms.Panel { Bounds = new Drawing.Rectangle(140, 30, 30, 25) };
            form.Controls.Add(sibling);
            surface.SnapToControls = true;
            surface.RefreshForm();

            at = OnForm(70, 55);
            window.MouseDown(at, MouseButton.Left);
            window.MouseMove(at + new Vector(21, 0));
            window.MouseUp(at + new Vector(21, 0), MouseButton.Left);
            Assert.Equal(60, panel.Left);
        }
        finally { window.Close(); form.Dispose(); }
    }

    [AvaloniaFact]
    public void GridSnapsResizeAndRoutedToolboxDropInContainerCoordinates()
    {
        var (window, surface, form, panel) = Open();
        try
        {
            surface.SnapToGrid = true;
            surface.SnapToControls = false;
            surface.Select(panel);
            var corner = OnForm(115, 80);
            window.MouseDown(corner, MouseButton.Left);
            window.MouseMove(corner + new Vector(10, 0));
            window.MouseUp(corner + new Vector(10, 0), MouseButton.Left);
            Assert.Equal(93, panel.Width);

            var received = new List<WinFormsControlDrop>();
            surface.ControlDropRequested += received.Add;
            var transferItem = new DataTransferItem();
            transferItem.Set(WinFormsToolboxDrag.Format, new WinFormsToolboxItem(typeof(Forms.Label)));
            var transfer = new DataTransfer();
            transfer.Add(transferItem);
            var host = Assert.IsType<Canvas>(surface.Content);
            var position = host.TranslatePoint(OnForm(35 + 15, 30 + 17), window)!.Value;
            surface.RaiseEvent(new DragEventArgs(DragDrop.DropEvent,
                transfer, window, position, KeyModifiers.None));

            var drop = Assert.Single(received);
            Assert.Same(panel, drop.Parent);
            Assert.Equal(new Drawing.Point(16, 16), drop.Location);
        }
        finally { window.Close(); form.Dispose(); }
    }

    [AvaloniaFact]
    public void NestedContainerGridPaintsInsideClientButLeavesBorderAndChildPixelsUntouched()
    {
        var (window, surface, form, panel) = Open();
        try
        {
            panel.BackColor = Drawing.Color.White;
            panel.BorderStyle = Forms.BorderStyle.FixedSingle;
            panel.Controls.Add(new Forms.Label
            {
                Bounds = new Drawing.Rectangle(8, 8, 30, 25),
                BackColor = Drawing.Color.Red,
            });
            surface.RefreshForm();

            var dot = ReadPixel(window, 12 + 35 + 1 + 48, 12 + 30 + 1 + 16);
            var child = ReadPixel(window, 12 + 35 + 1 + 16, 12 + 30 + 1 + 16);
            var border = ReadPixel(window, 12 + 35, 12 + 30 + 16);
            surface.ShowGrid = false;

            var hiddenDot = ReadPixel(window, 12 + 35 + 1 + 48, 12 + 30 + 1 + 16);
            Assert.True(dot.Red + 30 < hiddenDot.Red,
                "a nested container's client should show a raster grid dot");
            Assert.Equal(child, ReadPixel(window, 12 + 35 + 1 + 16, 12 + 30 + 1 + 16));
            Assert.Equal(border, ReadPixel(window, 12 + 35, 12 + 30 + 16));
        }
        finally { window.Close(); form.Dispose(); }
    }

    [AvaloniaFact]
    public void ResizeEdgeSnapsToNearbySiblingUnlessAltIsHeld()
    {
        var (window, surface, form, panel) = Open();
        try
        {
            form.Controls.Add(new Forms.Panel
            {
                Bounds = new Drawing.Rectangle(140, 30, 30, 25),
            });
            surface.SnapToGrid = true;
            surface.SnapToControls = true;
            surface.RefreshForm();
            surface.Select(panel);

            var right = OnForm(115, 55);
            window.MouseDown(right, MouseButton.Left);
            window.MouseMove(right + new Vector(21, 0));
            Assert.True(BlueNear(window, 152, 20),
                "a snapped resize edge should show the same blue control guide");
            window.MouseUp(right + new Vector(21, 0), MouseButton.Left);
            Assert.Equal(140, panel.Right);
            Assert.Equal(35, panel.Left);

            panel.Width = 80;
            surface.RefreshForm();
            window.MouseDown(right, MouseButton.Left);
            window.MouseMove(right + new Vector(21, 0), RawInputModifiers.Alt);
            window.MouseUp(right + new Vector(21, 0), MouseButton.Left);
            Assert.Equal(136, panel.Right);
            Assert.Equal(35, panel.Left);
        }
        finally { window.Close(); form.Dispose(); }
    }

    [AvaloniaFact]
    public void AFrontSiblingOccludesGridDotsFromTheContainerBehindIt()
    {
        var (window, surface, form, _) = Open();
        try
        {
            var front = new Forms.Label
            {
                Bounds = new Drawing.Rectangle(55, 45, 30, 25),
                BackColor = Drawing.Color.Red,
            };
            form.Controls.Add(front);
            form.Controls.SetChildIndex(front, 0);
            surface.RefreshForm();

            var covered = ReadPixel(window, 12 + 35 + 24, 12 + 30 + 16);
            surface.ShowGrid = false;
            Assert.Equal(covered, ReadPixel(window, 12 + 35 + 24, 12 + 30 + 16));
            Assert.True(covered.Red > covered.Green + 100,
                "the front sibling's red raster must remain above the rear panel grid");
        }
        finally { window.Close(); form.Dispose(); }
    }

    [AvaloniaFact]
    public void RulerTicksOccupyTheGutterWithoutChangingFormCoordinates()
    {
        var (window, surface, form, panel) = Open();
        try
        {
            Assert.True(surface.ShowRulers);
            var tick = ReadPixel(window, 52, 9);
            var blank = ReadPixel(window, 72, 9);
            Assert.True(tick.Red + 35 < blank.Red,
                $"ruler tick should be darker than its gutter: {tick}, {blank}");

            surface.ShowRulers = false;
            var hidden = ReadPixel(window, 52, 9);
            Assert.NotEqual(tick, hidden);
            Assert.Equal(new Drawing.Point(35, 30), panel.Location);
        }
        finally { window.Close(); form.Dispose(); }
    }

    [AvaloniaFact]
    public void ControlSnapShowsABlueGuideOnlyWhileDragging()
    {
        var (window, surface, form, panel) = Open();
        try
        {
            form.Controls.Add(new Forms.Panel
            {
                Bounds = new Drawing.Rectangle(140, 30, 30, 25),
            });
            surface.SnapToGrid = false;
            surface.SnapToControls = true;
            surface.RefreshForm();

            var at = OnForm(70, 55);
            window.MouseDown(at, MouseButton.Left);
            window.MouseMove(at + new Vector(21, 0));

            Assert.Equal(60, panel.Left);
            Assert.True(BlueNear(window, 152, 20),
                "the snapped sibling edge should be drawn as a blue line");

            window.MouseUp(at + new Vector(21, 0), MouseButton.Left);
            Assert.False(BlueNear(window, 152, 20),
                "the guide should disappear when the edit is complete");
        }
        finally { window.Close(); form.Dispose(); }
    }

    [AvaloniaFact]
    public void NestedControlGuideUsesItsParentClientCoordinates()
    {
        var (window, surface, form, panel) = Open();
        try
        {
            panel.Bounds = new Drawing.Rectangle(35, 30, 170, 100);
            var dragged = new Forms.Button
            {
                Bounds = new Drawing.Rectangle(10, 10, 30, 20),
            };
            var sibling = new Forms.Button
            {
                Bounds = new Drawing.Rectangle(80, 10, 30, 20),
            };
            panel.Controls.Add(dragged);
            panel.Controls.Add(sibling);
            surface.SnapToGrid = false;
            surface.SnapToControls = true;
            surface.RefreshForm();

            var at = OnForm(35 + 25, 30 + 20);
            window.MouseDown(at, MouseButton.Left);
            window.MouseMove(at + new Vector(36, 0));

            Assert.Equal(50, dragged.Left);
            Assert.True(BlueNear(window, 12 + 35 + 80, 45));

            window.MouseUp(at + new Vector(36, 0), MouseButton.Left);
            Assert.False(BlueNear(window, 12 + 35 + 80, 45));
        }
        finally { window.Close(); form.Dispose(); }
    }

    private static (Window Window, WinFormsDesignSurface Surface, Forms.Form Form, Forms.Panel Panel) Open()
    {
        var form = new Forms.Form
        {
            ClientSize = new Drawing.Size(220, 160),
            BackColor = Drawing.Color.White,
        };
        var panel = new Forms.Panel
        {
            Bounds = new Drawing.Rectangle(35, 30, 80, 50),
            BackColor = Drawing.Color.Red,
        };
        form.Controls.Add(panel);
        var surface = new WinFormsDesignSurface { RootForm = form };
        var window = new Window { Width = 320, Height = 250, Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, surface, form, panel);
    }

    private static Point OnForm(double x, double y) => new(x + 12, y + 12);

    private static int BrightnessNear(Window window, int dpi, int logicalX, int logicalY)
    {
        var scale = dpi / 96d;
        var width = (int)(320 * scale);
        var height = (int)(250 * scale);
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(dpi, dpi));
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height),
                handle.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally { handle.Free(); }

        var centreX = (int)Math.Round(logicalX * scale);
        var centreY = (int)Math.Round(logicalY * scale);
        var minimum = int.MaxValue;
        for (var y = centreY - 1; y <= centreY + 1; y++)
        {
            for (var x = centreX - 1; x <= centreX + 1; x++)
            {
                var offset = (y * width + x) * 4;
                minimum = Math.Min(minimum, pixels[offset]);
            }
        }

        return minimum;
    }

    private static (byte Red, byte Green, byte Blue) ReadPixel(Window window, int x, int y)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 250));
        bitmap.Render(window);
        var pixels = new byte[320 * 250 * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 320, 250),
                handle.AddrOfPinnedObject(), pixels.Length, 320 * 4);
        }
        finally { handle.Free(); }

        var offset = (y * 320 + x) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    private static bool BlueNear(Window window, int x, int y)
    {
        for (var near = x - 1; near <= x + 1; near++)
        {
            var pixel = ReadPixel(window, near, y);
            if (pixel.Blue > pixel.Red + 60 && pixel.Blue > pixel.Green + 25)
                return true;
        }

        return false;
    }
}
