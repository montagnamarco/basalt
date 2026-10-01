using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>A box the user cycles through a fixed list of text items with, instead of a number.</summary>
[DefaultProperty("Items")]
[DefaultEvent("SelectedItemChanged")]
[DefaultBindingProperty("SelectedItem")]
public partial class DomainUpDown : UpDownBase
{
    private readonly DomainUpDownItemCollection _items;

    private int _selectedIndex = -1;
    private bool _sorted;
    private bool _wrap;

    public DomainUpDown()
    {
        _items = new DomainUpDownItemCollection(this);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [Localizable(true)]
    public DomainUpDownItemCollection Items => _items;

    [Browsable(false)]
    [DefaultValue(-1)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= _items.Count) throw new ArgumentOutOfRangeException(nameof(value), value, "SelectedIndex must be -1 or a valid index into Items.");
            if (value == _selectedIndex) return;

            _selectedIndex = value;
            UpdateEditText();
            OnSelectedItemChanged(this, EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : _items.IndexOf(value);
    }

    [DefaultValue(false)]
    public bool Sorted
    {
        get => _sorted;
        set
        {
            if (value == _sorted) return;

            _sorted = value;
            if (value) SortItems();
        }
    }

    [Localizable(true)]
    [DefaultValue(false)]
    public bool Wrap
    {
        get => _wrap;
        set => _wrap = value;
    }

    public event EventHandler? SelectedItemChanged;

    protected void OnSelectedItemChanged(object? source, EventArgs e) => SelectedItemChanged?.Invoke(this, e);

    /// <summary>Every edit keystroke: a user-typed match against an item's text selects it at once, without waiting for Enter.</summary>
    protected override void OnChanged(object? source, EventArgs e)
    {
        if (!UserEdit) return;

        var index = IndexOfItemText(Text);
        if (index >= 0) SelectedIndex = index;
    }

    protected override void OnTextBoxKeyPress(object? source, KeyPressEventArgs e)
    {
        base.OnTextBoxKeyPress(source, e);
        if (e.Handled) return;

        // Enter commits whatever already matched; it never starts a new line in this single-line edit.
        if (e.KeyChar == '\r') e.Handled = true;
    }

    public override void UpButton() => Spin(forward: true);

    public override void DownButton() => Spin(forward: false);

    private void Spin(bool forward)
    {
        if (_items.Count == 0) return;

        if (_selectedIndex < 0)
        {
            SelectedIndex = forward ? 0 : _items.Count - 1;
            return;
        }

        var target = _selectedIndex + (forward ? 1 : -1);

        if (target >= _items.Count) target = _wrap ? 0 : _items.Count - 1;
        else if (target < 0) target = _wrap ? _items.Count - 1 : 0;

        SelectedIndex = target;
    }

    protected override void UpdateEditText()
    {
        ChangingText = true;
        Text = SelectedItem?.ToString() ?? string.Empty;
        ChangingText = false;
    }

    /// <summary>The index of the item whose text equals the given text, case-insensitively in the current culture; -1 when none matches.</summary>
    private int IndexOfItemText(string text)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i]?.ToString(), text, StringComparison.CurrentCultureIgnoreCase)) return i;
        }

        return -1;
    }

    /// <summary>Resorts the items alphabetically by their text, keeping whatever item was selected selected.</summary>
    private void SortItems()
    {
        var selected = SelectedItem;

        _items.Sort(ItemTextComparer.Instance);

        SelectedIndex = selected is null ? -1 : _items.IndexOf(selected);
    }

    /// <summary>Clamps SelectedIndex after the collection changed from under it; Items itself calls this.</summary>
    // Items edits keep the selection on the same item: an insert or removal
    // before it shifts the index silently, as nothing the user sees changes.
    internal void NotifyItemInserted(int index)
    {
        if (_selectedIndex >= 0 && index <= _selectedIndex) _selectedIndex++;
    }

    internal void NotifyItemRemoved(int index)
    {
        if (index < _selectedIndex)
        {
            _selectedIndex--;
            return;
        }

        // The selected item itself went: the selection moves to the item now
        // in its place (or the new last one), which the user does see change.
        if (index == _selectedIndex)
        {
            _selectedIndex = Math.Min(index, _items.Count - 1);
            UpdateEditText();
            OnSelectedItemChanged(this, EventArgs.Empty);
        }
    }

    internal void NotifyItemReplaced(int index)
    {
        if (index == _selectedIndex) UpdateEditText();
    }

    internal bool ShouldInsertSorted => _sorted;

    internal static int CompareItemText(object? left, object? right) =>
        string.Compare(left?.ToString(), right?.ToString(), CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);

    private sealed class ItemTextComparer : IComparer
    {
        public static readonly ItemTextComparer Instance = new();

        public int Compare(object? x, object? y) => CompareItemText(x, y);
    }

    public override string ToString() => $"{base.ToString()}, Items.Count: {_items.Count}";
}
