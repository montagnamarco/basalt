using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Text;

namespace System.Windows.Forms;

/// <summary>
/// A text box with a list the user drops down to pick from: a single line of
/// text, and either a popup list (<see cref="ComboBoxStyle.DropDown"/> and
/// <see cref="ComboBoxStyle.DropDownList"/>) or a list that never closes
/// (<see cref="ComboBoxStyle.Simple"/>).
/// </summary>
/// <remarks>
/// ComboBox is native in WinForms, like ListBox: its own window paints and
/// handles the mouse and keyboard, which is why the reference declarations
/// override OnMouseDown/OnKeyDown/OnKeyPress/OnKeyUp directly instead of
/// going through Control's DefaultMouseDown/DefaultKeyDown hooks (ComboBox is
/// one of the few native controls whose public class still overrides those
/// methods). Painting, by contrast, is not overridden there either (no
/// OnPaint), so it goes through DefaultPaint as every other native control's
/// does. The edit portion reuses the same TextEditor/TextView the text boxes
/// use (see ComboBox.Edit.cs) rather than a child control, exactly as
/// TextBoxBase hosts its own editing state without being a container.
/// </remarks>
[DefaultProperty("Items")]
[DefaultEvent("SelectedIndexChanged")]
public partial class ComboBox : ListControl
{
    // The single source of truth for the items: ObjectCollection reads and writes this directly.
    private readonly List<object> _items = [];

    private ObjectCollection? _itemsCollection;

    private int _selectedIndex = -1;
    private ComboBoxStyle _dropDownStyle = ComboBoxStyle.DropDown;
    private DrawMode _drawMode = DrawMode.Normal;
    private bool _sorted;
    private bool _integralHeight = true;
    private int _itemHeight;
    private int _maxDropDownItems = 8;
    private int _dropDownHeight = 106;
    private int _dropDownWidth;
    private int _updateCount;

    public ComboBox()
    {
        // The combo box paints and handles the mouse and keyboard itself, like ListBox and TextBox do.
        SetStyle(ControlStyles.UserPaint, false);
        _itemHeight = Math.Max(FontHeight, 1);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            // A combo box has no BorderStyle of its own: it is always the sunken classic edge.
            parameters.ExStyle |= WindowStyles.ExClientEdge;

            return parameters;
        }
    }

    protected override Size DefaultSize => new(121, PreferredHeight);

    [DefaultValue(DrawMode.Normal)]
    public DrawMode DrawMode
    {
        get => _drawMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DrawMode));
            if (value == _drawMode) return;

            _drawMode = value;
            Invalidate();
        }
    }

    [DefaultValue(106)]
    public int DropDownHeight
    {
        get => _dropDownHeight;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _dropDownHeight = value;
        }
    }

    [DefaultValue(ComboBoxStyle.DropDown)]
    public ComboBoxStyle DropDownStyle
    {
        get => _dropDownStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ComboBoxStyle));
            if (value == _dropDownStyle) return;

            if (DroppedDown) DroppedDown = false;

            _dropDownStyle = value;
            SyncTextWithSelection();
            Invalidate();
            OnDropDownStyleChanged(EventArgs.Empty);
        }
    }

    public int DropDownWidth
    {
        get => _dropDownWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _dropDownWidth = value;
        }
    }

    [DefaultValue(true)]
    public bool IntegralHeight
    {
        get => _integralHeight;
        set => _integralHeight = value;
    }

    public int ItemHeight
    {
        get => _itemHeight;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value == _itemHeight) return;

            _itemHeight = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObjectCollection Items => _itemsCollection ??= new ObjectCollection(this);

    [DefaultValue(8)]
    public int MaxDropDownItems
    {
        get => _maxDropDownItems;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxDropDownItems = value;
        }
    }

    [DefaultValue(0)]
    public int MaxLength
    {
        get => Editor.MaxLength == 32767 ? 0 : Editor.MaxLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Editor.MaxLength = value == 0 ? 32767 : value;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int PreferredHeight
    {
        get
        {
            var height = FontHeight;
            return height + NonClient.Vertical + 3;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= _items.Count) throw new ArgumentOutOfRangeException(nameof(value));
            SetSelectedIndexCore(value);
        }
    }

    [Browsable(false)]
    [Bindable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => _selectedIndex >= 0 ? _items[_selectedIndex] : null;
        set
        {
            if (value is null)
            {
                SelectedIndex = -1;
                return;
            }

            var index = _items.IndexOf(value);
            if (index >= 0) SelectedIndex = index;
        }
    }

    [DefaultValue(false)]
    public bool Sorted
    {
        get => _sorted;
        set
        {
            if (value == _sorted) return;

            _sorted = value;
            if (value) Sort();
        }
    }

    public void BeginUpdate()
    {
        _updateCount++;
        SuspendLayout();
    }

    public void EndUpdate()
    {
        if (_updateCount > 0) _updateCount--;

        ResumeLayout(_updateCount == 0);
        if (_updateCount == 0) Invalidate();
    }

    public int FindString(string? s) => FindStringCore(s, -1, exact: false);

    public int FindString(string? s, int startIndex) => FindStringCore(s, startIndex, exact: false);

    public int FindStringExact(string? s) => FindStringCore(s, -1, exact: true);

    public int FindStringExact(string? s, int startIndex) => FindStringCore(s, startIndex, exact: true);

    public int GetItemHeight(int index)
    {
        if (index < -1 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));

        return _itemHeight;
    }

    protected virtual void OnDropDownStyleChanged(EventArgs e) => ((EventHandler?)Events[s_dropDownStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnSelectedItemChanged(EventArgs e)
    {
        // No public event backs this hook, exactly as in WinForms: it exists only for a derived class to override.
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        ((EventHandler?)Events[s_selectedIndexChangedEvent])?.Invoke(this, e);
        OnSelectedItemChanged(e);
    }

    protected virtual void OnSelectionChangeCommitted(EventArgs e) => ((EventHandler?)Events[s_selectionChangeCommittedEvent])?.Invoke(this, e);

    /// <summary>Raised only when the user types or deletes in the edit, not when code sets Text or SelectedIndex.</summary>
    protected virtual void OnTextUpdate(EventArgs e) => ((EventHandler?)Events[s_textUpdateEvent])?.Invoke(this, e);

    protected override void RefreshItem(int index)
    {
        if (index < 0 || index >= _items.Count) return;

        Invalidate();
    }

    protected override void RefreshItems() => Invalidate();

    public override void ResetText() => Text = string.Empty;

    public void Select(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);

        if (length < 0)
        {
            var end = Math.Max(start + length, 0);
            Editor.Select(start, 0);
            Editor.MoveCaret(end, extend: true);
        }
        else
        {
            Editor.Select(start, length);
        }

        InvalidateEdit();
    }

    public void SelectAll()
    {
        Editor.Select(0, Editor.Length);
        InvalidateEdit();
    }

    protected override void SetItemCore(int index, object value)
    {
        if (index < 0 || index >= _items.Count || value is null) return;

        _items[index] = value;
        if (index == _selectedIndex) SyncTextWithSelection();
        Invalidate();
    }

    protected override void SetItemsCore(IList value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Items.ResetFromBinding(value);
    }

    public override string ToString() => $"{base.ToString()}, Items.Count: {_items.Count}";

    private static readonly object s_dropDownStyleChangedEvent = new();
    private static readonly object s_selectedIndexChangedEvent = new();
    private static readonly object s_selectionChangeCommittedEvent = new();
    private static readonly object s_textUpdateEvent = new();

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

    public event EventHandler? SelectionChangeCommitted
    {
        add => Events.AddHandler(s_selectionChangeCommittedEvent, value);
        remove => Events.RemoveHandler(s_selectionChangeCommittedEvent, value);
    }

    public event EventHandler? TextUpdate
    {
        add => Events.AddHandler(s_textUpdateEvent, value);
        remove => Events.RemoveHandler(s_textUpdateEvent, value);
    }

    /// <summary>Where a new SelectedIndex, from code or from a commit, applies itself: updates the text, repaints, raises the event.</summary>
    private void SetSelectedIndexCore(int value)
    {
        if (value == _selectedIndex) return;

        _selectedIndex = value;
        var textChanged = SetTextFromSelection();
        Invalidate();
        OnSelectedIndexChanged(EventArgs.Empty);
        if (textChanged) OnTextChanged(EventArgs.Empty);
    }

    /// <summary>Picks the index whose text matches, wrapping from startIndex; -1 (NoMatches) when nothing does.</summary>
    private int FindStringCore(string? text, int startIndex, bool exact)
    {
        if (startIndex < -1 || startIndex >= _items.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (_items.Count == 0) return -1;

        text ??= string.Empty;
        var count = _items.Count;

        for (var offset = 1; offset <= count; offset++)
        {
            var index = (startIndex + offset) % count;
            if (index < 0) index += count;

            var itemText = GetItemText(_items[index]);
            var matches = exact
                ? string.Equals(itemText, text, StringComparison.CurrentCultureIgnoreCase)
                : itemText.StartsWith(text, StringComparison.CurrentCultureIgnoreCase);

            if (matches) return index;
        }

        return -1;
    }

    private void Sort()
    {
        Items.SortEntries();
        Invalidate();
    }

    private Color EffectiveBackColor => CollectionVisualAppearance.Field(this,
        Enabled ? (ShouldSerializeBackColor() ? BackColor : SystemColors.Window) : SystemColors.Control);

    private Color EffectiveForeColor => CollectionVisualAppearance.Text(this,
        Enabled ? (ShouldSerializeForeColor() ? ForeColor : SystemColors.WindowText) : SystemColors.GrayText);
}
