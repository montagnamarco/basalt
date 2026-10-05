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

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public AutoCompleteStringCollection AutoCompleteCustomSource
    {
        get => TextBox.AutoCompleteCustomSource;
        set => TextBox.AutoCompleteCustomSource = value;
    }

    [DefaultValue(AutoCompleteMode.None)]
    public AutoCompleteMode AutoCompleteMode
    {
        get => TextBox.AutoCompleteMode;
        set => TextBox.AutoCompleteMode = value;
    }

    [DefaultValue(AutoCompleteSource.None)]
    public AutoCompleteSource AutoCompleteSource
    {
        get => TextBox.AutoCompleteSource;
        set => TextBox.AutoCompleteSource = value;
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
    public bool Multiline
    {
        get => TextBox.Multiline;
        set => TextBox.Multiline = value;
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

    [DefaultValue(true)]
    public bool ShortcutsEnabled
    {
        get => TextBox.ShortcutsEnabled;
        set => TextBox.ShortcutsEnabled = value;
    }

    /// <summary>The hosted edit control, for members this wrapper does not repeat.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextBox TextBox => (TextBox)Control;

    /// <summary>Named differently from the base item's own TextAlign (a ContentAlignment): this one is the edit control's HorizontalAlignment.</summary>
    [DefaultValue(HorizontalAlignment.Left)]
    [Localizable(true)]
    public HorizontalAlignment TextBoxTextAlign
    {
        get => TextBox.TextAlign;
        set => TextBox.TextAlign = value;
    }

    [Browsable(false)]
    public int TextLength => TextBox.TextLength;

    [DefaultValue(true)]
    public bool WordWrap
    {
        get => TextBox.WordWrap;
        set => TextBox.WordWrap = value;
    }

    public event EventHandler? AcceptsTabChanged
    {
        add => Events.AddHandler(s_acceptsTabChangedEvent, value);
        remove => Events.RemoveHandler(s_acceptsTabChangedEvent, value);
    }

    public event EventHandler? BorderStyleChanged
    {
        add => Events.AddHandler(s_borderStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_borderStyleChangedEvent, value);
    }

    public event EventHandler? HideSelectionChanged
    {
        add => Events.AddHandler(s_hideSelectionChangedEvent, value);
        remove => Events.RemoveHandler(s_hideSelectionChangedEvent, value);
    }

    public event EventHandler? ModifiedChanged
    {
        add => Events.AddHandler(s_modifiedChangedEvent, value);
        remove => Events.RemoveHandler(s_modifiedChangedEvent, value);
    }

    public event EventHandler? MultilineChanged
    {
        add => Events.AddHandler(s_multilineChangedEvent, value);
        remove => Events.RemoveHandler(s_multilineChangedEvent, value);
    }

    public event EventHandler? ReadOnlyChanged
    {
        add => Events.AddHandler(s_readOnlyChangedEvent, value);
        remove => Events.RemoveHandler(s_readOnlyChangedEvent, value);
    }

    public event EventHandler? TextBoxTextAlignChanged
    {
        add => Events.AddHandler(s_textBoxTextAlignChangedEvent, value);
        remove => Events.RemoveHandler(s_textBoxTextAlignChangedEvent, value);
    }

    private static readonly object s_acceptsTabChangedEvent = new();
    private static readonly object s_borderStyleChangedEvent = new();
    private static readonly object s_hideSelectionChangedEvent = new();
    private static readonly object s_modifiedChangedEvent = new();
    private static readonly object s_multilineChangedEvent = new();
    private static readonly object s_readOnlyChangedEvent = new();
    private static readonly object s_textBoxTextAlignChangedEvent = new();

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

    protected virtual void OnAcceptsTabChanged(EventArgs e) => ((EventHandler?)Events[s_acceptsTabChangedEvent])?.Invoke(this, e);

    public override Image? BackgroundImage
    {
        get => base.BackgroundImage;
        set => base.BackgroundImage = value;
    }

    public override ImageLayout BackgroundImageLayout
    {
        get => base.BackgroundImageLayout;
        set => base.BackgroundImageLayout = value;
    }

    protected virtual void OnBorderStyleChanged(EventArgs e) => ((EventHandler?)Events[s_borderStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnHideSelectionChanged(EventArgs e) => ((EventHandler?)Events[s_hideSelectionChangedEvent])?.Invoke(this, e);

    protected virtual void OnModifiedChanged(EventArgs e) => ((EventHandler?)Events[s_modifiedChangedEvent])?.Invoke(this, e);

    protected virtual void OnMultilineChanged(EventArgs e) => ((EventHandler?)Events[s_multilineChangedEvent])?.Invoke(this, e);

    protected virtual void OnReadOnlyChanged(EventArgs e) => ((EventHandler?)Events[s_readOnlyChangedEvent])?.Invoke(this, e);

    /// <summary>On top of the base's generic Enter/KeyDown/Validating wiring, the edit control's own property-changed events are forwarded too.</summary>
    protected override void OnSubscribeControlEvents(Control? control)
    {
        base.OnSubscribeControlEvents(control);

        if (control is not TextBox textBox) return;

        textBox.AcceptsTabChanged += HandleAcceptsTabChanged;
        textBox.BorderStyleChanged += HandleBorderStyleChanged;
        textBox.HideSelectionChanged += HandleHideSelectionChanged;
        textBox.ModifiedChanged += HandleModifiedChanged;
        textBox.MultilineChanged += HandleMultilineChanged;
        textBox.ReadOnlyChanged += HandleReadOnlyChanged;
        textBox.TextAlignChanged += HandleTextBoxTextAlignChanged;
    }

    protected override void OnUnsubscribeControlEvents(Control? control)
    {
        base.OnUnsubscribeControlEvents(control);

        if (control is not TextBox textBox) return;

        textBox.AcceptsTabChanged -= HandleAcceptsTabChanged;
        textBox.BorderStyleChanged -= HandleBorderStyleChanged;
        textBox.HideSelectionChanged -= HandleHideSelectionChanged;
        textBox.ModifiedChanged -= HandleModifiedChanged;
        textBox.MultilineChanged -= HandleMultilineChanged;
        textBox.ReadOnlyChanged -= HandleReadOnlyChanged;
        textBox.TextAlignChanged -= HandleTextBoxTextAlignChanged;
    }

    private void HandleAcceptsTabChanged(object? sender, EventArgs e) => OnAcceptsTabChanged(e);

    private void HandleBorderStyleChanged(object? sender, EventArgs e) => OnBorderStyleChanged(e);

    private void HandleHideSelectionChanged(object? sender, EventArgs e) => OnHideSelectionChanged(e);

    private void HandleModifiedChanged(object? sender, EventArgs e) => OnModifiedChanged(e);

    private void HandleMultilineChanged(object? sender, EventArgs e) => OnMultilineChanged(e);

    private void HandleReadOnlyChanged(object? sender, EventArgs e) => OnReadOnlyChanged(e);

    private void HandleTextBoxTextAlignChanged(object? sender, EventArgs e) =>
        ((EventHandler?)Events[s_textBoxTextAlignChangedEvent])?.Invoke(this, e);

    public void Paste() => TextBox.Paste();

    public void ScrollToCaret() => TextBox.ScrollToCaret();

    public void Select(int start, int length) => TextBox.Select(start, length);

    public void SelectAll() => TextBox.SelectAll();

    public void Undo() => TextBox.Undo();
}
