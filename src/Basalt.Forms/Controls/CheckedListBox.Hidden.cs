using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Members CheckedListBox re-declares only to keep the public shape
/// Microsoft declares on this type: data binding is hidden (a checked list
/// of arbitrary bound objects is not what this control is for), and the
/// paint and input hooks below need no behaviour beyond ListBox's own.
/// </summary>
/// <remarks>
/// WmReflectCommand and WndProc are skipped: both only make sense once
/// Control.WndProc (p0-control.txt) exists to reflect native notifications.
/// </remarks>
public partial class CheckedListBox
{
    private bool _useCompatibleTextRendering;

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void OnBackColorChanged(EventArgs e) => base.OnBackColorChanged(e);

    protected override void OnClick(EventArgs e) => base.OnClick(e);

    protected override void OnDrawItem(DrawItemEventArgs e) => base.OnDrawItem(e);

    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnKeyPress(KeyPressEventArgs e) => base.OnKeyPress(e);

    protected override void OnMeasureItem(MeasureItemEventArgs e) => base.OnMeasureItem(e);

    protected override void OnSelectedIndexChanged(EventArgs e) => base.OnSelectedIndexChanged(e);

    protected override CreateParams CreateParams => base.CreateParams;

    public override int ItemHeight
    {
        get => base.ItemHeight;
        set => base.ItemHeight = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new object? DataSource
    {
        get => base.DataSource;
        set => base.DataSource = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new string DisplayMember
    {
        get => base.DisplayMember;
        set => base.DisplayMember = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new string ValueMember
    {
        get => base.ValueMember;
        set => base.ValueMember = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Padding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    /// <summary>Kept for source compatibility; the one skin this renders in never needed GDI+ text measuring.</summary>
    [DefaultValue(false)]
    public bool UseCompatibleTextRendering
    {
        get => _useCompatibleTextRendering;
        set
        {
            if (value == _useCompatibleTextRendering) return;

            _useCompatibleTextRendering = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? Click
    {
        add => base.Click += value;
        remove => base.Click -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DataSourceChanged
    {
        add => base.DataSourceChanged += value;
        remove => base.DataSourceChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DisplayMemberChanged
    {
        add => base.DisplayMemberChanged += value;
        remove => base.DisplayMemberChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event DrawItemEventHandler? DrawItem
    {
        add => base.DrawItem += value;
        remove => base.DrawItem -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event MeasureItemEventHandler? MeasureItem
    {
        add => base.MeasureItem += value;
        remove => base.MeasureItem -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event MouseEventHandler? MouseClick
    {
        add => base.MouseClick += value;
        remove => base.MouseClick -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? ValueMemberChanged
    {
        add => base.ValueMemberChanged += value;
        remove => base.ValueMemberChanged -= value;
    }
}
