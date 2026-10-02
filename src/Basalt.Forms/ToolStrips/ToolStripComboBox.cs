using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A <see cref="ComboBox"/> hosted on a <see cref="ToolStrip"/>.</summary>
[DefaultProperty("Items")]
public class ToolStripComboBox : ToolStripControlHost
{
    public ToolStripComboBox()
        : base(new ComboBox())
    {
        SubscribeComboBoxEvents();
    }

    public ToolStripComboBox(string? name)
        : this()
    {
        Name = name;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public ToolStripComboBox(Control c)
        : base(c)
    {
        SubscribeComboBoxEvents();
    }

    /// <summary>The hosted combo, for members this wrapper does not repeat.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ComboBox ComboBox => (ComboBox)Control;

    protected override Padding DefaultMargin => new(1, 0, 1, 0);

    // Control is still null the first time the base ToolStripItem() constructor reads this.
    protected override Size DefaultSize => Control is null ? new Size(100, 22) : new Size(100, ComboBox.PreferredHeight);

    [DefaultValue(106)]
    public int DropDownHeight
    {
        get => ComboBox.DropDownHeight;
        set => ComboBox.DropDownHeight = value;
    }

    [DefaultValue(ComboBoxStyle.DropDown)]
    public ComboBoxStyle DropDownStyle
    {
        get => ComboBox.DropDownStyle;
        set => ComboBox.DropDownStyle = value;
    }

    public int DropDownWidth
    {
        get => ComboBox.DropDownWidth;
        set => ComboBox.DropDownWidth = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DroppedDown
    {
        get => ComboBox.DroppedDown;
        set => ComboBox.DroppedDown = value;
    }

    [DefaultValue(true)]
    public bool IntegralHeight
    {
        get => ComboBox.IntegralHeight;
        set => ComboBox.IntegralHeight = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ComboBox.ObjectCollection Items => ComboBox.Items;

    [DefaultValue(8)]
    public int MaxDropDownItems
    {
        get => ComboBox.MaxDropDownItems;
        set => ComboBox.MaxDropDownItems = value;
    }

    [DefaultValue(0)]
    public int MaxLength
    {
        get => ComboBox.MaxLength;
        set => ComboBox.MaxLength = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => ComboBox.SelectedIndex;
        set => ComboBox.SelectedIndex = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => ComboBox.SelectedItem;
        set => ComboBox.SelectedItem = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedText
    {
        get => ComboBox.SelectedText;
        set => ComboBox.SelectedText = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionLength
    {
        get => ComboBox.SelectionLength;
        set => ComboBox.SelectionLength = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionStart
    {
        get => ComboBox.SelectionStart;
        set => ComboBox.SelectionStart = value;
    }

    [DefaultValue(false)]
    public bool Sorted
    {
        get => ComboBox.Sorted;
        set => ComboBox.Sorted = value;
    }

    public event EventHandler? DropDown
    {
        add => Events.AddHandler(s_dropDownEvent, value);
        remove => Events.RemoveHandler(s_dropDownEvent, value);
    }

    public event EventHandler? DropDownClosed
    {
        add => Events.AddHandler(s_dropDownClosedEvent, value);
        remove => Events.RemoveHandler(s_dropDownClosedEvent, value);
    }

    public event EventHandler? DropDownStyleChanged
    {
        add => Events.AddHandler(s_dropDownStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_dropDownStyleChangedEvent, value);
    }

    public event EventHandler? SelectedIndexChanged
    {
        add => Events.AddHandler(s_selectedIndexChangedEvent, value);
        remove => Events.RemoveHandler(s_selectedIndexChangedEvent, value);
    }

    public event EventHandler? TextUpdate
    {
        add => Events.AddHandler(s_textUpdateEvent, value);
        remove => Events.RemoveHandler(s_textUpdateEvent, value);
    }

    private static readonly object s_dropDownEvent = new();
    private static readonly object s_dropDownClosedEvent = new();
    private static readonly object s_dropDownStyleChangedEvent = new();
    private static readonly object s_selectedIndexChangedEvent = new();
    private static readonly object s_textUpdateEvent = new();

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

    /// <summary>A combo box's own double-click opens its drop-down; this item-level event is hidden, not removed.</summary>
    public new event EventHandler? DoubleClick
    {
        add => base.DoubleClick += value;
        remove => base.DoubleClick -= value;
    }

    public void BeginUpdate() => ComboBox.BeginUpdate();

    public void EndUpdate() => ComboBox.EndUpdate();

    public int FindString(string? s) => ComboBox.FindString(s);

    public int FindString(string? s, int startIndex) => ComboBox.FindString(s, startIndex);

    public int FindStringExact(string? s) => ComboBox.FindStringExact(s);

    public int FindStringExact(string? s, int startIndex) => ComboBox.FindStringExact(s, startIndex);

    public int GetItemHeight(int index) => ComboBox.GetItemHeight(index);

    public override Size GetPreferredSize(Size constrainingSize) => ComboBox.GetPreferredSize(constrainingSize);

    public void Select(int start, int length) => ComboBox.Select(start, length);

    public void SelectAll() => ComboBox.SelectAll();

    protected virtual void OnDropDown(EventArgs e) => ((EventHandler?)Events[s_dropDownEvent])?.Invoke(this, e);

    protected virtual void OnDropDownClosed(EventArgs e) => ((EventHandler?)Events[s_dropDownClosedEvent])?.Invoke(this, e);

    protected virtual void OnDropDownStyleChanged(EventArgs e) => ((EventHandler?)Events[s_dropDownStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnSelectedIndexChanged(EventArgs e) => ((EventHandler?)Events[s_selectedIndexChangedEvent])?.Invoke(this, e);

    /// <summary>
    /// The exact moment a selection commits (a click, Enter, or an arrow key -
    /// not merely highlighting an item while the list is open): a seam a
    /// subclass overrides, same as Microsoft's own has no public event of its
    /// own at this level either.
    /// </summary>
    protected virtual void OnSelectionChangeCommitted(EventArgs e)
    {
    }

    /// <summary>On top of the base's generic Enter/KeyDown/Validating wiring, the combo's own drop-down and selection events are forwarded too.</summary>
    protected override void OnSubscribeControlEvents(Control? control)
    {
        base.OnSubscribeControlEvents(control);

        if (control is not ComboBox comboBox) return;

        comboBox.SelectionChangeCommitted += HandleSelectionChangeCommitted;
    }

    protected override void OnUnsubscribeControlEvents(Control? control)
    {
        base.OnUnsubscribeControlEvents(control);

        if (control is not ComboBox comboBox) return;

        comboBox.SelectionChangeCommitted -= HandleSelectionChangeCommitted;
    }

    protected virtual void OnTextUpdate(EventArgs e) => ((EventHandler?)Events[s_textUpdateEvent])?.Invoke(this, e);

    public override string ToString() => $"{base.ToString()}, Items.Count: {Items.Count}";

    private void HandleSelectionChangeCommitted(object? sender, EventArgs e) => OnSelectionChangeCommitted(e);

    /// <summary>Forwards the hosted ComboBox's own drop-down and selection events to this item's events.</summary>
    private void SubscribeComboBoxEvents()
    {
        ComboBox.DropDown += (_, e) => OnDropDown(e);
        ComboBox.DropDownClosed += (_, e) => OnDropDownClosed(e);
        ComboBox.DropDownStyleChanged += (_, e) => OnDropDownStyleChanged(e);
        ComboBox.SelectedIndexChanged += (_, e) => OnSelectedIndexChanged(e);
        ComboBox.TextUpdate += (_, e) => OnTextUpdate(e);
    }
}
