using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Painting;

/// <summary>
/// A control whose paint throws while it is being designed is drawn as the designer's error frame, and
/// the rest of the window still draws; outside design mode the exception propagates, as in WinForms.
/// Proved by drawing into bitmaps and reading their pixels.
/// </summary>
/// <remarks>
/// Red-proof, reintroduced once and run: with the paint boundary in RecordClient not catching (the
/// try/catch removed), the design-mode tests fail with the InvalidOperationException out of DrawToBitmap,
/// and the sibling test never reaches its pixel check. With the exception filter forced to true, the
/// propagation test fails because the exception is swallowed outside design mode.
/// </remarks>
public class DesignTimePaintFailureTests
{
    private const string Message = "Painting failed on purpose.";

    /// <summary>Where the failing paint is: the background, the OnPaint override, or the Paint event.</summary>
    public enum Phase { Background, Foreground, PaintEvent }

    /// <summary>A control whose paint throws on its second paint, in the chosen phase. Blue otherwise.</summary>
    private sealed class FailingControl : Control
    {
        private readonly Phase _phase;
        private int _failingPaints;

        public FailingControl(Phase phase)
        {
            _phase = phase;
            BackColor = Color.Blue;
            Paint += (_, _) =>
            {
                if (_phase == Phase.PaintEvent) Fail();
            };
        }

        /// <summary>How many times the failing phase has run.</summary>
        public int Paints => _failingPaints;

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            if (_phase == Phase.Background) Fail();
            base.OnPaintBackground(pevent);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_phase == Phase.Foreground) Fail();
            base.OnPaint(e);
        }

        private void Fail()
        {
            if (++_failingPaints == 2) throw new InvalidOperationException(Message);
        }
    }

    /// <summary>A site that reports design mode, and offers one service: the designer's sink.</summary>
    private sealed class DesignSite(bool designMode, object? service = null) : ISite
    {
        public IComponent Component => null!;
        public IContainer? Container => null;
        public bool DesignMode => designMode;
        public string? Name { get; set; }
        public object? GetService(Type serviceType) => service is not null && serviceType.IsInstanceOfType(service) ? service : null;
    }

    private sealed class RecordingSink : IDesignTimePaintFailureSink
    {
        public List<(Control Control, Exception Exception)> Failures { get; } = [];

        public void PaintFailed(Control control, Exception exception) => Failures.Add((control, exception));
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    private static int CountDarkPixels(Bitmap bitmap, Rectangle area)
    {
        var count = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R < 80 && pixel.G < 80 && pixel.B < 80) count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The frame on a 400 by 300 control: the X from the top-left corner passes (300, 225), the X from the
    /// top-right corner passes (300, 74), the red border runs along the top, and the blue fill is gone.
    /// </summary>
    private static void AssertErrorFrame(Bitmap bitmap)
    {
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(300, 225).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(300, 74).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(200, 0).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(350, 100).ToArgb());
        Assert.True(CountDarkPixels(bitmap, new Rectangle(0, 0, 300, 50)) > 0, "The exception's type and message are not drawn.");
    }

    [Theory]
    [InlineData(Phase.Background)]
    [InlineData(Phase.Foreground)]
    [InlineData(Phase.PaintEvent)]
    public void A_design_mode_control_whose_paint_throws_on_its_second_paint_is_drawn_as_an_error_frame(Phase phase)
    {
        var sink = new RecordingSink();
        var control = new FailingControl(phase) { Size = new Size(400, 300) };
        control.Site = new DesignSite(designMode: true, sink);

        using (var first = Draw(control))
        {
            Assert.Equal(Color.Blue.ToArgb(), first.GetPixel(350, 100).ToArgb());
        }

        control.Invalidate();
        using var bitmap = Draw(control);

        AssertErrorFrame(bitmap);

        // The designer hears of the failure once, with the control and the exception.
        var failure = Assert.Single(sink.Failures);
        Assert.Same(control, failure.Control);
        Assert.Equal(Message, failure.Exception.Message);
    }

    [Fact]
    public void The_next_paint_after_an_error_frame_runs_the_handlers_again()
    {
        var control = new FailingControl(Phase.Foreground) { Size = new Size(400, 300) };
        control.Site = new DesignSite(designMode: true);
        using (Draw(control))
        {
        }

        control.Invalidate();
        using (Draw(control))
        {
        }

        control.Invalidate();
        using var bitmap = Draw(control);

        // The third paint runs OnPaint again and does not throw: its blue fill replaces the frame.
        Assert.Equal(3, control.Paints);
        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(350, 100).ToArgb());
    }

    [Fact]
    public void A_failing_control_does_not_stop_its_siblings_from_drawing()
    {
        var parent = new Control { Size = new Size(400, 300), BackColor = Color.White };
        var broken = new FailingControl(Phase.Foreground) { Bounds = new Rectangle(0, 0, 200, 300), Parent = parent };
        broken.Site = new DesignSite(designMode: true);
        _ = new Control { Bounds = new Rectangle(200, 0, 200, 300), BackColor = Color.Lime, Parent = parent };

        using (Draw(parent))
        {
        }

        // Invalidating the parent with its children repaints both in one pass; the broken one fails in it.
        parent.Invalidate(true);
        using var bitmap = Draw(parent);

        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(300, 150).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(100, 0).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(150, 100).ToArgb());
    }

    /// <summary>
    /// A child without a site, under a control the designer hosts, is contained too: its failing paint draws the
    /// error frame in its own area, and its sibling still draws. The design mode is that of an ancestor.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the paint boundary checking the control's own site only (Site?.DesignMode == true), the
    /// child's exception was not contained and escaped Draw, so the test failed with the child's InvalidOperationException
    /// before any pixel assertion ran.
    /// </remarks>
    [Fact]
    public void An_unsited_child_of_a_design_mode_control_is_contained_and_its_sibling_still_draws()
    {
        var parent = new Control { Size = new Size(400, 300), BackColor = Color.White };
        parent.Site = new DesignSite(designMode: true);
        var broken = new FailingControl(Phase.Foreground) { Bounds = new Rectangle(0, 0, 200, 300), Parent = parent };
        _ = new Control { Bounds = new Rectangle(200, 0, 200, 300), BackColor = Color.Lime, Parent = parent };

        using (Draw(parent))
        {
        }

        broken.Invalidate();
        using var bitmap = Draw(parent);

        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(300, 150).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(100, 0).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(150, 100).ToArgb());
    }

    /// <summary>
    /// The designer hears of a contained failure of an unsited child through the site of its nearest sited ancestor:
    /// the child has no site, so the notice used to go nowhere, and the failure never reached Output.
    /// </summary>
    /// <remarks>
    /// Red-proof: with NotifyDesignTimePaintFailed reading only this control's own Site, the sink received nothing
    /// and Assert.Single failed with "The collection was empty" (9 of the 10 tests of the class still passed).
    /// </remarks>
    [Fact]
    public void A_contained_failure_of_an_unsited_child_is_reported_through_the_nearest_ancestor_site()
    {
        var sink = new RecordingSink();
        var parent = new Control { Size = new Size(400, 300), BackColor = Color.White };
        parent.Site = new DesignSite(designMode: true, sink);
        var child = new FailingControl(Phase.Foreground) { Bounds = new Rectangle(0, 0, 200, 300), Parent = parent };
        Assert.Null(child.Site);

        using (Draw(parent))
        {
        }

        child.Invalidate();
        using var bitmap = Draw(parent);
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(100, 0).ToArgb());

        var failure = Assert.Single(sink.Failures);
        Assert.Same(child, failure.Control);
        Assert.Equal(Message, failure.Exception.Message);
    }

    [Theory]
    [InlineData(Phase.Background)]
    [InlineData(Phase.Foreground)]
    [InlineData(Phase.PaintEvent)]
    public void Outside_design_mode_the_paint_exception_propagates_and_the_designer_is_not_told(Phase phase)
    {
        var sink = new RecordingSink();
        var control = new FailingControl(phase) { Size = new Size(400, 300) };
        control.Site = new DesignSite(designMode: false, sink);
        using (Draw(control))
        {
        }

        control.Invalidate();
        var thrown = Assert.Throws<InvalidOperationException>(() => Draw(control));

        Assert.Equal(Message, thrown.Message);
        Assert.Empty(sink.Failures);
    }
}
