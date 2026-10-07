using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

/// <summary>
/// The rulers' zero tick must line up with the client origin the grid's dots
/// are measured from (see WinFormsDesignerRulers.cs). Once the design surface
/// started drawing the form's own border and caption (Form.DesignerFrame.cs),
/// the dotted client area moved past the surface's twelve-pixel gutter by the
/// form's NonClient frame, and WinFormsDesignerGrid followed; the ruler has to
/// use the same offset; or its 0 keeps reading where the gutter ends rather
/// than where the grid actually starts counting from.
///
/// Proved by rasterising a real window, not by reading NonClient off the
/// form. The first dot itself is found by diffing a render with the grid on
/// against one with it off, so a border bevel or caption pixel that merely
/// looks grey is never mistaken for it.
/// </summary>
public sealed class WinFormsDesignerRulerAlignmentTests
{
    private const int WindowWidth = 320;
    private const int WindowHeight = 260;

    /// <summary>The grid's own cell size (WinFormsDesignerGrid._gridSize's default): the first
    /// dot sits one cell in from the origin the ruler's zero is meant to mark.</summary>
    private const int GridCell = 8;

    private static (Window Window, WinFormsDesignSurface Surface) Open()
    {
        var form = new Forms.Form
        {
            ClientSize = new Drawing.Size(220, 160),
            BackColor = Drawing.Color.White,
            FormBorderStyle = Forms.FormBorderStyle.Sizable,
            ControlBox = true,
            Text = "Hello",
        };
        var surface = new WinFormsDesignSurface { RootForm = form };
        var window = new Window { Width = WindowWidth, Height = WindowHeight, Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, surface);
    }

    private static byte[] RenderToBgra(Window window)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(WindowWidth, WindowHeight));
        bitmap.Render(window);
        var pixels = new byte[WindowWidth * WindowHeight * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, WindowWidth, WindowHeight),
                handle.AddrOfPinnedObject(), pixels.Length, WindowWidth * 4);
        }
        finally { handle.Free(); }
        return pixels;
    }

    private static (byte Red, byte Green, byte Blue) PixelAt(byte[] bgra, int x, int y)
    {
        var offset = (y * WindowWidth + x) * 4;
        return (bgra[offset + 2], bgra[offset + 1], bgra[offset]);
    }

    /// <summary>
    /// The tick colour the rulers paint (see WinFormsDesignerRulers.TickBrush):
    /// close enough in Manhattan distance to read back as "a tick is here"
    /// without depending on antialiasing giving the exact RGB.
    /// </summary>
    private static bool IsTick((byte Red, byte Green, byte Blue) c) =>
        Math.Abs(c.Red - 86) + Math.Abs(c.Green - 86) + Math.Abs(c.Blue - 86) < 40;

    /// <summary>
    /// The x of the ruler's zero tick: the first vertical tick mark in the
    /// horizontal ruler's band, scanning left to right from the window edge.
    /// No "0" is ever printed next to it (WinFormsDesignerRulers skips the
    /// label at x == 0), but the tick itself is drawn there like every other.
    /// </summary>
    private static int RulerZeroTickX(byte[] bgra)
    {
        for (var x = 0; x < WindowWidth; x++)
            if (IsTick(PixelAt(bgra, x, 9))) return x;

        throw new InvalidOperationException("no tick was painted in the horizontal ruler's band");
    }

    /// <summary>
    /// Where the very first grid dot was painted: the topmost, then
    /// leftmost, pixel that differs between a render with the grid shown and
    /// one with it switched off. The border, the caption and the client's
    /// own white background are identical in both renders and so cancel out
    /// of the diff entirely - only the dots the grid itself drew are left.
    /// </summary>
    private static (int X, int Y) FirstGridDotPosition(byte[] withGrid, byte[] withoutGrid)
    {
        for (var y = 0; y < WindowHeight; y++)
            for (var x = 0; x < WindowWidth; x++)
                if (PixelAt(withGrid, x, y) != PixelAt(withoutGrid, x, y))
                    return (x, y);

        throw new InvalidOperationException("the grid painted no dot at all to diff against");
    }

    [AvaloniaFact]
    public void TheRulersZeroTickIsOneGridCellLeftOfTheFirstDot()
    {
        var (window, surface) = Open();
        try
        {
            var withGrid = RenderToBgra(window);
            var rulerZeroTickX = RulerZeroTickX(withGrid);

            surface.ShowGrid = false;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var withoutGrid = RenderToBgra(window);

            var (dotX, _) = FirstGridDotPosition(withGrid, withoutGrid);

            // WinFormsDesignerGrid starts its dots one cell in from the
            // client origin (its loop begins at x = gridSize.Width, not at
            // x = 0), so the ruler's own zero - the same origin - sits
            // exactly one grid cell to the left of that first dot. Before
            // the ruler carried the form's NonClient offset, this gap was
            // wider by the whole non-client frame instead.
            Assert.True(Math.Abs(rulerZeroTickX + GridCell - dotX) <= 1,
                $"the ruler's 0 tick (x={rulerZeroTickX}) plus one grid cell ({GridCell}) "
              + $"should land on the first dot (x={dotX})");
        }
        finally { window.Close(); }
    }
}
