using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

// These concrete declarations retain the control-wide layout, input and
// notification paths while exposing the same override points as WinForms.
public abstract partial class ButtonBase
{
    protected override ImeMode DefaultImeMode => ImeMode.Disable;
}

public partial class Button
{
    protected override void RescaleConstantsForDpi(int oldDpi, int newDpi) => base.RescaleConstantsForDpi(oldDpi, newDpi);
}

public partial class CheckBox
{
    protected override void RescaleConstantsForDpi(int oldDpi, int newDpi) => base.RescaleConstantsForDpi(oldDpi, newDpi);
}

public partial class RadioButton
{
    protected override void RescaleConstantsForDpi(int oldDpi, int newDpi) => base.RescaleConstantsForDpi(oldDpi, newDpi);
}

public partial class ListBox
{
    protected override void OnChangeUICues(UICuesEventArgs e) => base.OnChangeUICues(e);
    protected override void RescaleConstantsForDpi(int oldDpi, int newDpi) => base.RescaleConstantsForDpi(oldDpi, newDpi);
}

public partial class Label
{
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    protected override ImeMode DefaultImeMode => ImeMode.Disable;
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
}

public partial class PictureBox
{
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    protected override ImeMode DefaultImeMode => ImeMode.Disable;
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
}

public partial class ProgressBar
{
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    protected override ImeMode DefaultImeMode => ImeMode.Disable;
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
}

public abstract partial class ScrollBar
{
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    protected override ImeMode DefaultImeMode => ImeMode.Disable;
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
}

public partial class TrackBar
{
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    protected override ImeMode DefaultImeMode => ImeMode.Disable;
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
    protected override void OnSystemColorsChanged(EventArgs e) => base.OnSystemColorsChanged(e);
}

public partial class MonthCalendar
{
    protected override ImeMode DefaultImeMode => ImeMode.Disable;
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    protected override void RescaleConstantsForDpi(int oldDpi, int newDpi) => base.RescaleConstantsForDpi(oldDpi, newDpi);
}

public partial class DateTimePicker
{
    protected override void OnSystemColorsChanged(EventArgs e) => base.OnSystemColorsChanged(e);
}

public partial class PropertyGrid
{
    protected override bool ShowFocusCues => base.ShowFocusCues;
    protected override void OnSystemColorsChanged(EventArgs e) => base.OnSystemColorsChanged(e);
}

public abstract partial class TextBoxBase
{
    protected override bool CanEnableIme => !ReadOnly && base.CanEnableIme;
    protected override ImeMode ImeModeBase { get => base.ImeModeBase; set => base.ImeModeBase = value; }
}

public abstract partial class UpDownBase
{
    public new DockPaddingEdges DockPadding => base.DockPadding;
    protected override void RescaleConstantsForDpi(int oldDpi, int newDpi) => base.RescaleConstantsForDpi(oldDpi, newDpi);
}

public partial class StatusBar
{
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
}

public partial class ToolBar
{
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
}

public partial class PrintPreviewDialog
{
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
}

public sealed partial class MdiClient
{
    public override Image? BackgroundImage { get => base.BackgroundImage; set => base.BackgroundImage = value; }
    public override ImageLayout BackgroundImageLayout { get => base.BackgroundImageLayout; set => base.BackgroundImageLayout = value; }
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) => base.ScaleControl(factor, specified);
    protected override void ScaleCore(float x, float y) => base.ScaleCore(x, y);
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, height, specified);
}

public partial class Splitter
{
    protected override ImeMode DefaultImeMode => ImeMode.Disable;
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, height, specified);
}

public partial class SplitContainer
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    protected override void OnKeyDown(KeyEventArgs e) => base.OnKeyDown(e);
    protected override void OnKeyUp(KeyEventArgs e) => base.OnKeyUp(e);
    protected override void OnMouseLeave(EventArgs e) => base.OnMouseLeave(e);
    protected override void OnMove(EventArgs e) => base.OnMove(e);
    protected override void OnRightToLeftChanged(EventArgs e) => base.OnRightToLeftChanged(e);
    protected override bool ProcessTabKey(bool forward) => base.ProcessTabKey(forward);
    protected override void Select(bool directed, bool forward) => base.Select(directed, forward);
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, height, specified);
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        var distance = SplitterDistance;
        base.ScaleControl(factor, specified);
        var scale = Orientation == Orientation.Vertical ? factor.Width : factor.Height;
        var inset = _borderStyle == BorderStyle.Fixed3D ? 2 : _borderStyle == BorderStyle.FixedSingle ? 1 : 0;
        _knownExtent = (Orientation == Orientation.Vertical ? ClientSize.Width : ClientSize.Height) - 2 * inset;
        SplitterDistance = (int)Math.Round(distance * scale);
    }
}

public partial class TableLayoutPanel
{
    protected override void OnLayout(LayoutEventArgs e) => base.OnLayout(e);
    protected override void ScaleCore(float x, float y) => base.ScaleCore(x, y);
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        SuspendLayout();
        try
        {
            foreach (ColumnStyle style in ColumnStyles)
                if (style.SizeType == SizeType.Absolute) style.Width *= factor.Width;
            foreach (RowStyle style in RowStyles)
                if (style.SizeType == SizeType.Absolute) style.Height *= factor.Height;
            base.ScaleControl(factor, specified);
        }
        finally
        {
            ResumeLayout();
        }
    }
}

public partial class UserControl
{
    protected override void OnResize(EventArgs e) => base.OnResize(e);
}

public partial class ToolStrip
{
    protected override void RescaleConstantsForDpi(int oldDpi, int newDpi) => base.RescaleConstantsForDpi(oldDpi, newDpi);
}

public partial class ToolStripContainer
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    protected override ControlCollection CreateControlsInstance() => base.CreateControlsInstance();
    protected override void OnRightToLeftChanged(EventArgs e) => base.OnRightToLeftChanged(e);
    protected override void OnSizeChanged(EventArgs e) => base.OnSizeChanged(e);
}

public partial class ToolStripContentPanel
{
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var args = new ToolStripContentPanelRenderEventArgs(e.Graphics, this);
        Renderer.DrawToolStripContentPanelBackground(args);

        if (!args.Handled) base.OnPaintBackground(e);
    }
}

public abstract partial class ToolStripItem
{
    protected virtual bool ShowKeyboardCues => SystemInformation.MenuAccessKeysUnderlined;
}

public partial class ToolStripDropDown
{
    public new event UICuesEventHandler? ChangeUICues { add => base.ChangeUICues += value; remove => base.ChangeUICues -= value; }
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    public new event EventHandler? RegionChanged { add => base.RegionChanged += value; remove => base.RegionChanged -= value; }
    public new Region? Region { get => base.Region; set => base.Region = value; }
}

public partial class ToolStripDropDownMenu
{
    protected override void SetDisplayedItems() => PerformLayout();
}

public partial class ToolStripOverflow
{
    protected override void SetDisplayedItems() => PerformLayout();
}
