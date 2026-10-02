using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A <see cref="TextBox"/> hosted on a <see cref="ToolStrip"/>, as a real, focusable text field.</summary>
public class ToolStripTextBox : ToolStripControlHost
{
    public ToolStripTextBox()
        : base(new TextBox())
    {
        BorderStyle = BorderStyle.Fixed3D;
    }

    public ToolStripTextBox(string? name)
        : this()
    {
        Name = name;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public ToolStripTextBox(Control c)
        : base(c)
    {
    }

    [DefaultValue(false)]
    public bool AcceptsReturn
    {
        get => TextBox.AcceptsReturn;
        set => TextBox.AcceptsReturn = value;
    }

    [DefaultValue(false)]
    public bool AcceptsTab
    {
        get => TextBox.AcceptsTab;
        set => TextBox.AcceptsTab = value;
    }

    [DefaultValue(BorderStyle.Fixed3D)]
    public BorderStyle BorderStyle
    {
        get => TextBox.BorderStyle;
        set => TextBox.BorderStyle = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CanUndo => TextBox.CanUndo;

    [DefaultValue(CharacterCasing.Normal)]
    public CharacterCasing CharacterCasing
    {
        get => TextBox.CharacterCasing;
        set => TextBox.CharacterCasing = value;
    }

    protected override Padding DefaultMargin => new(1, 0, 1, 0);

    // Control is still null the first time the base ToolStripItem() constructor reads this.
    protected override Size DefaultSize => Control is null ? new Size(100, 22) : new Size(100, TextBox.PreferredHeight);

    [DefaultValue(true)]
    public bool HideSelection
    {
        get => TextBox.HideSelection;
        set => TextBox.HideSelection = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string[] Lines
    {
        get => TextBox.Lines;
        set => TextBox.Lines = value;
    }

    [DefaultValue(32767)]
    public int MaxLength
    {
        get => TextBox.MaxLength;
        set => TextBox.MaxLength = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Modified
    {
        get => TextBox.Modified;
        set => TextBox.Modified = value;
    }

    [DefaultValue(false)]
    public bool ReadOnly
    {
        get => TextBox.ReadOnly;
        set => TextBox.ReadOnly = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedText
    {
        get => TextBox.SelectedText;
        set => TextBox.SelectedText = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionLength
    {
        get => TextBox.SelectionLength;
        set => TextBox.SelectionLength = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionStart
    {
        get => TextBox.SelectionStart;
        set => TextBox.SelectionStart = value;
    }

    /// <summary>The hosted edit control, for members this wrapper does not repeat.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextBox TextBox => (TextBox)Control;

    [Browsable(false)]
    public int TextLength => TextBox.TextLength;

    public void AppendText(string? text) => TextBox.AppendText(text);

    public void Clear() => TextBox.Clear();

    public void ClearUndo() => TextBox.ClearUndo();

    public void Copy() => TextBox.Copy();

    public void Cut() => TextBox.Cut();

    public void DeselectAll() => TextBox.DeselectAll();

    public char GetCharFromPosition(Point pt) => TextBox.GetCharFromPosition(pt);

    public int GetCharIndexFromPosition(Point pt) => TextBox.GetCharIndexFromPosition(pt);

    public int GetFirstCharIndexFromLine(int lineNumber) => TextBox.GetFirstCharIndexFromLine(lineNumber);

    public int GetFirstCharIndexOfCurrentLine() => TextBox.GetFirstCharIndexOfCurrentLine();

    public int GetLineFromCharIndex(int index) => TextBox.GetLineFromCharIndex(index);

    public Point GetPositionFromCharIndex(int index) => TextBox.GetPositionFromCharIndex(index);

    public override Size GetPreferredSize(Size constrainingSize) => TextBox.GetPreferredSize(constrainingSize);

    public void Paste() => TextBox.Paste();

    public void ScrollToCaret() => TextBox.ScrollToCaret();

    public void Select(int start, int length) => TextBox.Select(start, length);

    public void SelectAll() => TextBox.SelectAll();

    public void Undo() => TextBox.Undo();
}
