using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Controls;

public class ControlApiDeclarationsTests
{
    [Fact]
    public void ReadOnlyTextBoxDisablesImeAndNonTextControlsDefaultToDisabled()
    {
        using var text = new ImeTextBox();
        Assert.True(text.ImeAvailable);
        text.ReadOnly = true;
        Assert.False(text.ImeAvailable);
        Assert.Equal(ImeMode.Disable, new Label().ImeMode);
        Assert.Equal(ImeMode.Disable, new MonthCalendar().ImeMode);
        Assert.Equal(ImeMode.Disable, new Button().ImeMode);
        var calendar = new MonthCalendar();
        var changes = 0;
        calendar.ImeModeChanged += (_, _) => changes++;
        calendar.ImeMode = ImeMode.On;
        Assert.Equal(ImeMode.On, ((Control)calendar).ImeMode);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void TableScalingScalesAbsoluteStylesButPreservesPercentages()
    {
        using var table = new TableLayoutPanel { Size = new Size(200, 100), ColumnCount = 2, RowCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Scale(new SizeF(2, 3));
        Assert.Equal(60, table.ColumnStyles[0].Width);
        Assert.Equal(60, table.RowStyles[0].Height);
        Assert.Equal(100, table.ColumnStyles[1].Width);
        Assert.Equal(100, table.RowStyles[1].Height);
    }

    [Fact]
    public void ScalingSplitContainerScalesSplitterPositionAlongItsOrientation()
    {
        using var split = new SplitContainer { Size = new Size(200, 100), SplitterDistance = 50 };
        split.Scale(new SizeF(2, 2));
        Assert.Equal(100, split.SplitterDistance);
        Assert.Equal(400, split.Width);
    }

    [Fact]
    public void ScalingConstraintsCanRetainHostSuppliedBounds()
    {
        using var container = new ScalingContainer
        {
            Size = new Size(100, 100), MinimumSize = new Size(80, 80), MaximumSize = new Size(200, 200),
        };
        container.ScaleConstraints(2, 3, false);
        Assert.Equal(new Size(160, 240), container.MinimumSize);
        Assert.Equal(new Size(400, 600), container.MaximumSize);
        Assert.Equal(new Size(100, 100), container.Size);
        container.ScaleConstraints(1, 1, true);
        Assert.Equal(new Size(160, 240), container.Size);
    }

    [Fact]
    public void FormAutoSizeFitsChildrenBeforeTheFormIsShownAndGrowOnlyDoesNotShrink()
    {
        using var form = new Form { ClientSize = new Size(300, 200), AutoSizeMode = AutoSizeMode.GrowAndShrink };
        form.Controls.Add(new Panel { Location = new Point(20, 30), Size = new Size(100, 50), Margin = Padding.Empty });
        form.AutoSize = true;
        Assert.Equal(new Size(120, 80), form.ClientSize);
        form.AutoSizeMode = AutoSizeMode.GrowOnly;
        form.Controls[0].Size = new Size(40, 20);
        form.PerformLayout();
        Assert.Equal(new Size(120, 80), form.ClientSize);
        Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => form.AutoSizeMode = (AutoSizeMode)12);
    }

    [Fact]
    public void ContentPanelInvokesItsRendererAndPaintsItsResult()
    {
        using var panel = new ToolStripContentPanel { Size = new Size(40, 30) };
        var renderer = new ToolStripSystemRenderer();
        renderer.RenderToolStripContentPanelBackground += (_, e) =>
        {
            e.Graphics.Clear(Color.Magenta);
            e.Handled = true;
        };
        panel.Renderer = renderer;
        using var bitmap = new Bitmap(panel.Width, panel.Height);
        panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, panel.Size));
        Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(15, 15).ToArgb());
    }

    [AvaloniaFact]
    public void MaximizedBoundsConstrainTheActualHostAndRaiseOneChangeEvent()
    {
        using var form = new BoundsForm();
        var changes = 0;
        form.MaximizedBoundsChanged += (_, _) => changes++;
        var bounds = new Rectangle(20, 25, 400, 300);
        form.CustomMaximizedBounds = bounds;
        form.CustomMaximizedBounds = bounds;
        Assert.Equal(1, changes);
        form.Show();
        form.WindowState = FormWindowState.Maximized;
        Assert.Equal(bounds, form.Bounds);
        Assert.True(form.Host!.MaxWidth <= 400);
        Assert.True(form.Host.MaxHeight <= 300);
        var targetSize = Size.Empty;
        Assert.True(form.DpiSize(96, 144, ref targetSize));
        Assert.Equal(new Size(600, 450), targetSize);
    }

    [AvaloniaFact]
    public void EmbeddedFrameColorsPaintPixelsAndNativeSettingsRejectUnsupportedValues()
    {
        using var form = new Form { TopLevel = false, Size = new Size(240, 160), Text = "x", ControlBox = false };
        var changes = 0;
        form.FormCaptionBackColorChanged += (_, _) => changes++;
        form.FormCaptionBackColor = Color.Magenta;
        form.FormCaptionBackColor = Color.Magenta;
        form.FormBorderColor = Color.Lime;
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(150, 12).ToArgb());
        Assert.Equal(1, changes);
        Assert.Throws<PlatformNotSupportedException>(() => form.TopLevel = true);
        using var native = new Form();
        Assert.Throws<PlatformNotSupportedException>(() => native.FormCaptionBackColor = Color.Red);
        Assert.True(native.FormCaptionBackColor.IsEmpty);
        Assert.Throws<PlatformNotSupportedException>(() => native.FormScreenCaptureMode = ScreenCaptureMode.HideWindow);
        Assert.Equal(ScreenCaptureMode.Allow, native.FormScreenCaptureMode);
    }

    [AvaloniaFact]
    public void EmbeddedMoveGestureRaisesBeginAndEndIncludingCaptureLoss()
    {
        using var form = new Form { TopLevel = false, Size = new Size(240, 160), ControlBox = false, Text = "x" };
        var began = 0;
        var ended = 0;
        form.ResizeBegin += (_, _) => began++;
        form.ResizeEnd += (_, _) => ended++;
        Assert.True(form.NonClientMouseDown(MouseButtons.Left, new Point(20, 12)));
        form.NonClientMouseMove(new Point(30, 15));
        form.NonClientMouseUp(new Point(30, 15));
        Assert.Equal(1, began);
        Assert.Equal(1, ended);
        Assert.True(form.NonClientMouseDown(MouseButtons.Left, new Point(20, 12)));
        form.DefaultCaptureLost();
        form.DefaultCaptureLost();
        Assert.Equal(2, began);
        Assert.Equal(2, ended);
    }

    private sealed class ImeTextBox : TextBox
    {
        public bool ImeAvailable => CanEnableIme;
    }

    private sealed class ScalingContainer : ContainerControl
    {
        public void ScaleConstraints(float x, float y, bool resize) => ScaleMinMaxSize(x, y, resize);
    }

    private sealed class BoundsForm : Form
    {
        public Rectangle CustomMaximizedBounds { get => MaximizedBounds; set => MaximizedBounds = value; }
        public bool DpiSize(int oldDpi, int newDpi, ref Size size) => OnGetDpiScaledSize(oldDpi, newDpi, ref size);
    }
}
