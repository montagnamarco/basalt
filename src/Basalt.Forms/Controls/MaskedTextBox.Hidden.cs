using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Members MaskedTextBox re-declares only to keep the public shape
/// Microsoft declares on this type: most forward to TextBoxBase. Multiline
/// is the one real divergence, since a masked box's single line of fields
/// never wraps, exactly as WinForms refuses to turn it on.
/// </summary>
/// <remarks>WndProc is skipped: it overrides a Control member p0-control.txt lists as not yet implemented.</remarks>
public partial class MaskedTextBox
{
    public override char GetCharFromPosition(Point pt) => base.GetCharFromPosition(pt);

    public override int GetCharIndexFromPosition(Point pt) => base.GetCharIndexFromPosition(pt);

    public new int GetFirstCharIndexFromLine(int lineNumber) => base.GetFirstCharIndexFromLine(lineNumber);

    public new int GetFirstCharIndexOfCurrentLine() => base.GetFirstCharIndexOfCurrentLine();

    public override int GetLineFromCharIndex(int index) => base.GetLineFromCharIndex(index);

    public override Point GetPositionFromCharIndex(int index) => base.GetPositionFromCharIndex(index);

    public new void ScrollToCaret() => base.ScrollToCaret();

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new bool AcceptsTab
    {
        get => base.AcceptsTab;
        set => base.AcceptsTab = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? AcceptsTabChanged
    {
        add => base.AcceptsTabChanged += value;
        remove => base.AcceptsTabChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public new string[] Lines
    {
        get => base.Lines;
        set => base.Lines = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int MaxLength
    {
        get => base.MaxLength;
        set => base.MaxLength = value;
    }

    /// <summary>Always false: a masked box's fields sit on one line, so turning this on is refused, as WinForms does.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override bool Multiline
    {
        get => false;
        set
        {
            if (value) throw new InvalidOperationException("MaskedTextBox does not support multiline input.");
        }
    }

    protected override void OnMultilineChanged(EventArgs e) => base.OnMultilineChanged(e);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? MultilineChanged
    {
        add => base.MultilineChanged += value;
        remove => base.MultilineChanged -= value;
    }

    protected override void OnTextChanged(EventArgs e) => base.OnTextChanged(e);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new bool ReadOnly
    {
        get => base.ReadOnly;
        set => base.ReadOnly = value;
    }

    [Browsable(false)]
    public override int TextLength => base.TextLength;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new bool WordWrap
    {
        get => base.WordWrap;
        set => base.WordWrap = value;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void CreateHandle() => base.CreateHandle();

    protected override bool IsInputKey(Keys keyData) => base.IsInputKey(keyData);

    protected override void OnBackColorChanged(EventArgs e) => base.OnBackColorChanged(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnKeyUp(KeyEventArgs e) => base.OnKeyUp(e);

    protected override void OnMouseDown(MouseEventArgs e) => base.OnMouseDown(e);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => base.ProcessCmdKey(ref msg, keyData);

    protected override bool ProcessKeyMessage(ref Message m) => base.ProcessKeyMessage(ref m);

    protected override CreateParams CreateParams => base.CreateParams;
}
