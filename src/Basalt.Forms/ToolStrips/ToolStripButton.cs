using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A push button on a <see cref="ToolStrip"/>, optionally a check button.</summary>
public class ToolStripButton : ToolStripItem
{
    private bool _checkOnClick;
    private CheckState _checkState = CheckState.Unchecked;

    public ToolStripButton()
    {
    }

    public ToolStripButton(string? text)
        : base(text, null, null)
    {
    }

    public ToolStripButton(Image? image)
        : base(null, image, null)
    {
    }

    public ToolStripButton(string? text, Image? image)
        : base(text, image, null)
    {
    }

    public ToolStripButton(string? text, Image? image, EventHandler? onClick)
        : base(text, image, onClick)
    {
    }

    public ToolStripButton(string? text, Image? image, EventHandler? onClick, string? name)
        : base(text, image, onClick, name)
    {
    }

    [DefaultValue(true)]
    public new bool AutoToolTip
    {
        get => base.AutoToolTip;
        set => base.AutoToolTip = value;
    }

    protected override bool DefaultAutoToolTip => true;

    [DefaultValue(false)]
    public bool CheckOnClick
    {
        get => _checkOnClick;
        set => _checkOnClick = value;
    }

    [DefaultValue(CheckState.Unchecked)]
    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            if (value == _checkState) return;

            _checkState = value;
            OnCheckStateChanged(EventArgs.Empty);
            OnCheckedChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checkState != CheckState.Unchecked;
        set => CheckState = value ? CheckState.Checked : CheckState.Unchecked;
    }

    public event EventHandler? CheckStateChanged;

    public event EventHandler? CheckedChanged;

    /// <summary>A checked toggle button surfaces that the same way a check box does: as an extra accessible state bit.</summary>
    protected override AccessibleObject CreateAccessibilityInstance()
    {
        var accessibleObject = new ToolStripItemAccessibleObject(this);
        if (Checked) accessibleObject.AddState(AccessibleStates.Checked);

        return accessibleObject;
    }

    public override Size GetPreferredSize(Size constrainingSize) => base.GetPreferredSize(constrainingSize);

    protected virtual void OnCheckStateChanged(EventArgs e) => CheckStateChanged?.Invoke(this, e);

    protected virtual void OnCheckedChanged(EventArgs e) => CheckedChanged?.Invoke(this, e);

    /// <summary>A click toggles the check state first, when the button asks for that, then raises Click as usual.</summary>
    protected override void OnClick(EventArgs e)
    {
        if (_checkOnClick) Checked = !Checked;

        base.OnClick(e);
    }

    /// <summary>Space clicks a button the same way Enter does; the base class only wires Enter.</summary>
    protected internal override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Space)
        {
            PerformClick();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    /// <summary>The flat face, raised while hovered, sunken while pressed or checked, as a classic toolbar button.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var renderer = Owner?.Renderer;
        if (renderer is null)
        {
            base.OnPaint(e);
            return;
        }

        renderer.DrawButtonBackground(new ToolStripItemRenderEventArgs(e.Graphics, this));
        ToolStripItemPainting.DrawImageAndText(renderer, e.Graphics, this);

        base.OnPaint(e);
    }
}
