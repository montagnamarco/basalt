using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

/// <summary>
/// The design surface draws the form being edited with its own window frame
/// (border and title bar) around the dotted client area, the way Visual
/// Studio and VB6 show the form under design - see
/// src/Basalt.Forms/Form.DesignerFrame.cs. Every claim here is a pixel read,
/// not a property check: NonClient geometry alone does not prove anything
/// was actually painted.
/// </summary>
public sealed class WinFormsDesignerFrameTests
{
    private const int Gutter = 12;

    private static (Window Window, WinFormsDesignSurface Surface, Forms.Form Form) Open(
        Forms.FormBorderStyle borderStyle = Forms.FormBorderStyle.Sizable, bool controlBox = true, string text = "Hello")
    {
        var form = new Forms.Form
        {
            ClientSize = new Drawing.Size(220, 160),
            BackColor = Drawing.Color.White,
            FormBorderStyle = borderStyle,
            ControlBox = controlBox,
            Text = text,
        };
        var surface = new WinFormsDesignSurface { RootForm = form };
        var window = new Window { Width = 320, Height = 260, Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, surface, form);
    }

    private static (byte Red, byte Green, byte Blue) ReadPixel(Window window, int x, int y)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 260));
        bitmap.Render(window);
        var pixels = new byte[320 * 260 * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 320, 260),
                handle.AddrOfPinnedObject(), pixels.Length, 320 * 4);
        }
        finally { handle.Free(); }

        var offset = (y * 320 + x) * 4;
        return (pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static int Distance((byte Red, byte Green, byte Blue) a, (byte Red, byte Green, byte Blue) b) =>
        Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);

    [AvaloniaFact]
    public void CaptionBarSitsAboveTheDottedClientAreaWithSampleCaptionColours()
    {
        var (window, _, _) = Open();
        try
        {
            // Pure caption background: inside the title bar band (host y 16..34),
            // clear of the short "Hello" title and of the control box on the right.
            var caption = ReadPixel(window, 150, 20);
            var white = (Red: (byte)255, Green: (byte)255, Blue: (byte)255);
            Assert.True(Distance(caption, white) > 20,
                $"the caption band should not be the plain white client colour: {caption}");

            // Inside the client area (past the border and caption), the designer
            // paints the form's own white background.
            var client = ReadPixel(window, 25, 45);
            Assert.True(Distance(client, white) < 10, $"the client area should read back white: {client}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CaptionTextInkIsPresentAndRerendersWhenTextChangesLikeAPropertyEdit()
    {
        var (window, surface, form) = Open(text: "");
        try
        {
            var blankRow = Row(window);

            // What the Properties panel's own edit ultimately does to a selected
            // form: set the CLR property, then ask the surface to repaint.
            form.Text = "Hello";
            surface.RefreshForm();
            var helloRow = Row(window);
            Assert.NotEqual(blankRow, helloRow);

            form.Text = "Goodbye";
            surface.RefreshForm();
            var goodbyeRow = Row(window);
            Assert.NotEqual(helloRow, goodbyeRow);
        }
        finally { window.Close(); }

        static (byte, byte, byte)[] Row(Window window) =>
            Enumerable.Range(16, 60).Select(x => ReadPixel(window, x, 20)).ToArray();
    }

    [AvaloniaFact]
    public void ControlBoxGlyphAreaIsPaintedAtTheTopRightAndGoesAwayWithoutAControlBox()
    {
        // The gradient caption background varies left to right, not top to
        // bottom, so a button's face is compared against the same column
        // just above the button row rather than against a distant column.
        var (window, _, _) = Open(controlBox: true);
        var aboveGlyph = ReadPixel(window, 226, 17);
        var glyphArea = ReadPixel(window, 226, 24);
        window.Close();

        Assert.True(Distance(glyphArea, aboveGlyph) > 20,
            $"a control box button face should stand out from the plain gradient above it: glyph={glyphArea}, above={aboveGlyph}");

        var (noBoxWindow, _, _) = Open(controlBox: false);
        var noBoxAboveGlyph = ReadPixel(noBoxWindow, 226, 17);
        var noBoxGlyphArea = ReadPixel(noBoxWindow, 226, 24);
        noBoxWindow.Close();

        Assert.True(Distance(noBoxGlyphArea, noBoxAboveGlyph) < 20,
            $"without a control box nothing should be painted over the gradient there: glyph={noBoxGlyphArea}, above={noBoxAboveGlyph}");
    }

    [AvaloniaFact]
    public void FormBorderStyleNoneHidesTheCaptionAndTheBorder()
    {
        var (window, _, _) = Open(borderStyle: Forms.FormBorderStyle.None);
        try
        {
            var white = (Red: (byte)255, Green: (byte)255, Blue: (byte)255);

            // With no frame, the outer corner and what would have been the
            // caption band are both the form's own white client background.
            var corner = ReadPixel(window, Gutter, Gutter);
            Assert.True(Distance(corner, white) < 10, $"no border should be painted: {corner}");

            var wouldBeCaption = ReadPixel(window, 150, 20);
            Assert.True(Distance(wouldBeCaption, white) < 10, $"no caption should be painted: {wouldBeCaption}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ResizingTheRootFormWritesClientSizeNotSize()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        var before = session.Form.Width - session.Form.ClientSize.Width;
        var beforeHeight = session.Form.Height - session.Form.ClientSize.Height;

        // The bounds a bottom-right drag on the design surface would propose for the
        // root form's outer rectangle (see WinFormsDesignSurface's gesture handling,
        // which already assigns ClientSize while dragging): SetBoundsAsync must turn
        // that into the same ClientSize assignment Visual Studio writes for a form,
        // never a Size assignment (which would include the border and caption VS
        // never counts as part of a form's own size in generated code).
        await session.SetBoundsAsync(session.Form, new Drawing.Rectangle(0, 0, 700, 560));

        Assert.Equal(new Drawing.Size(700 - before, 560 - beforeHeight), session.Form.ClientSize);
        Assert.Contains($"Me.ClientSize = New System.Drawing.Size({700 - before}, {560 - beforeHeight})",
            session.Document.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Me.Size = New System.Drawing.Size", session.Document.Source, StringComparison.Ordinal);
    }
}
