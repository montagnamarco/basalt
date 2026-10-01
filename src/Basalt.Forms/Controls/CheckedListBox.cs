using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// A <see cref="ListBox"/> that shows a check box in front of every item, so
/// the user can tick several of them independently of which one is selected.
/// </summary>
/// <remarks>
/// Check states are kept by position, beside the items, and follow them
/// through ListBox's insert, remove, clear and sort notifications: two rows
/// holding equal items (two "Apple" strings) have a check box each.
/// </remarks>
[DefaultProperty("Items")]
public partial class CheckedListBox : ListBox
{
    /// <summary>The check state of the item at the same index in Items.</summary>
    private readonly List<CheckState> _checkStates = [];

    private CheckedIndexCollection? _checkedIndicesCollection;
    private CheckedItemCollection? _checkedItemsCollection;

    private bool _threeDCheckBoxes;

    /// <summary>
    /// Whether a single click toggles the check box of the item it hits.
    /// False (the default): a click only selects the item; a further click
    /// on the item that is already selected toggles it instead.
    /// </summary>
    [DefaultValue(false)]
    public bool CheckOnClick { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CheckedIndexCollection CheckedIndices => _checkedIndicesCollection ??= new CheckedIndexCollection(this);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CheckedItemCollection CheckedItems => _checkedItemsCollection ??= new CheckedItemCollection(this);

    /// <summary>CheckedListBox always draws Normal items; setting this does nothing, as in WinForms.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override DrawMode DrawMode
    {
        get => DrawMode.Normal;
        set { }
    }

    [Localizable(true)]
    public new ObjectCollection Items => (ObjectCollection)base.Items;

    /// <summary>Only One and None make sense with a check box: the others are rejected, as in WinForms.</summary>
    public override SelectionMode SelectionMode
    {
        get => base.SelectionMode;
        set
        {
            if (value is not (SelectionMode.None or SelectionMode.One))
            {
                throw new ArgumentException("CheckedListBox only supports SelectionMode.One or SelectionMode.None.", nameof(value));
            }

            base.SelectionMode = value;
        }
    }

    /// <summary>
    /// Whether the check box has the classic sunken 3D look (true) or a flat
    /// outline (false, the default).
    /// </summary>
    [DefaultValue(false)]
    public bool ThreeDCheckBoxes
    {
        get => _threeDCheckBoxes;
        set
        {
            if (value == _threeDCheckBoxes) return;

            _threeDCheckBoxes = value;
            Invalidate();
        }
    }

    public event ItemCheckEventHandler? ItemCheck;

    protected override ListBox.ObjectCollection CreateItemCollection() => new ObjectCollection(this);

    /// <summary>Whether the item is checked (Checked or Indeterminate) or not.</summary>
    public bool GetItemChecked(int index) => GetItemCheckState(index) != CheckState.Unchecked;

    public CheckState GetItemCheckState(int index)
    {
        if (index < 0 || index >= Items.Count) throw new ArgumentOutOfRangeException(nameof(index));

        return _checkStates[index];
    }

    protected virtual void OnItemCheck(ItemCheckEventArgs ice) => ItemCheck?.Invoke(this, ice);

    /// <summary>Check states and the SelectionMode.None current item are reset by ListBox's item notifications, not here.</summary>
    protected override void RefreshItems() => base.RefreshItems();

    // Check states and the current item follow their rows through every change to Items.

    internal override void ItemInsertedInternal(int index)
    {
        _checkStates.Insert(index, CheckState.Unchecked);
        if (_currentIndex >= index) _currentIndex++;
    }

    internal override void ItemRemovedInternal(int index)
    {
        _checkStates.RemoveAt(index);

        if (_currentIndex == index) _currentIndex = Math.Min(index, _checkStates.Count - 1);
        else if (_currentIndex > index) _currentIndex--;
    }

    internal override void ItemsClearedInternal()
    {
        _checkStates.Clear();
        _currentIndex = -1;
    }

    internal override void ItemsResetInternal(int count)
    {
        _checkStates.Clear();
        _checkStates.AddRange(Enumerable.Repeat(CheckState.Unchecked, count));
        _currentIndex = -1;
    }

    internal override void ItemsReorderedInternal(int[] oldIndexAt)
    {
        var reordered = oldIndexAt.Select(oldIndex => _checkStates[oldIndex]).ToArray();
        _checkStates.Clear();
        _checkStates.AddRange(reordered);

        if (_currentIndex >= 0) _currentIndex = Array.IndexOf(oldIndexAt, _currentIndex);
    }

    public void SetItemChecked(int index, bool value) => SetItemCheckState(index, value ? CheckState.Checked : CheckState.Unchecked);

    /// <summary>
    /// Raises <see cref="ItemCheck"/> before applying the change, so a handler
    /// can replace <see cref="ItemCheckEventArgs.NewValue"/> and have that
    /// value take effect instead of the one requested.
    /// </summary>
    public void SetItemCheckState(int index, CheckState value)
    {
        if (index < 0 || index >= Items.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(CheckState));

        var current = GetItemCheckState(index);
        var args = new ItemCheckEventArgs(index, value, current);
        OnItemCheck(args);

        if (args.NewValue == current) return;

        StoreCheckState(index, args.NewValue);
        Invalidate(GetItemRectangle(index));
    }

    /// <summary>Records a check state without raising ItemCheck: how a newly added item gets its initial state.</summary>
    private void SetInitialCheckState(int index, CheckState state) => _checkStates[index] = state;

    private void StoreCheckState(int index, CheckState value) => _checkStates[index] = value;
}
