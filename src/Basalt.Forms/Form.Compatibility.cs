using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

public partial class Form
{
    public override bool AutoScroll
    {
        get => base.AutoScroll;
        set => base.AutoScroll = value;
    }

    public override AutoValidate AutoValidate
    {
        get => base.AutoValidate;
        set => base.AutoValidate = value;
    }

    public override Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    // Form declares these virtual slots in the WinForms contract. Their
    // shared implementation already performs layout, painting and events.
    protected override void AdjustFormScrollbars(bool displayScrollbars) => base.AdjustFormScrollbars(displayScrollbars);
    protected override Control.ControlCollection CreateControlsInstance() => new ControlCollection(this);
    protected override void OnBackgroundImageChanged(EventArgs e) => base.OnBackgroundImageChanged(e);
    protected override void OnBackgroundImageLayoutChanged(EventArgs e) => base.OnBackgroundImageLayoutChanged(e);
    protected override void OnCreateControl() => base.OnCreateControl();
    protected override void OnEnabledChanged(EventArgs e) => base.OnEnabledChanged(e);
    protected override void OnEnter(EventArgs e) => base.OnEnter(e);
    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);
    protected override void OnGotFocus(EventArgs e) => base.OnGotFocus(e);
    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);
    protected override void OnHandleDestroyed(EventArgs e) => base.OnHandleDestroyed(e);
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        ApplyFormAutoSize();
    }
    protected override void OnPaint(PaintEventArgs e) => base.OnPaint(e);
    protected override void OnResize(EventArgs e) => base.OnResize(e);
    protected override void OnStyleChanged(EventArgs e) => base.OnStyleChanged(e);
    protected override void OnVisibleChanged(EventArgs e) => base.OnVisibleChanged(e);
    protected override bool ProcessDialogChar(char charCode) => base.ProcessDialogChar(charCode);
    protected internal override bool ProcessMnemonic(char charCode) => base.ProcessMnemonic(charCode);
}
