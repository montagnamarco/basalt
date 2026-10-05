using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>LabelEdit: the in-place TextBox a user opens over an item's label, committed with Enter or lost focus, cancelled with Escape.</summary>
public partial class ListView
{
    private bool _labelEdit;
    private ListViewLabelEditBox? _editBox;
    private ListViewItem? _editingItem;

    [DefaultValue(false)]
    public bool LabelEdit
    {
        get => _labelEdit;
        set => _labelEdit = value;
    }

    public event LabelEditEventHandler? AfterLabelEdit
    {
        add => Events.AddHandler(s_afterLabelEditEvent, value);
        remove => Events.RemoveHandler(s_afterLabelEditEvent, value);
    }

    public event LabelEditEventHandler? BeforeLabelEdit
    {
        add => Events.AddHandler(s_beforeLabelEditEvent, value);
        remove => Events.RemoveHandler(s_beforeLabelEditEvent, value);
    }

    private static readonly object s_afterLabelEditEvent = new();
    private static readonly object s_beforeLabelEditEvent = new();

    protected virtual void OnAfterLabelEdit(LabelEditEventArgs e) => ((LabelEditEventHandler?)Events[s_afterLabelEditEvent])?.Invoke(this, e);

    protected virtual void OnBeforeLabelEdit(LabelEditEventArgs e) => ((LabelEditEventHandler?)Events[s_beforeLabelEditEvent])?.Invoke(this, e);

    /// <summary>Opens the in-place edit over one item's label; called by ListViewItem.BeginEdit and by F2/click-to-edit.</summary>
    internal void BeginEditCore(ListViewItem item)
    {
        if (ReferenceEquals(_editingItem, item)) return;
        if (IndexOfItem(item) < 0) return;

        EndEditCore(commit: true);

        var args = new LabelEditEventArgs(IndexOfItem(item), item.Text);
        OnBeforeLabelEdit(args);
        if (args.CancelEdit) return;

        _editingItem = item;
        _editBox ??= CreateEditBox();

        _editBox.Bounds = GetItemRect(IndexOfItem(item), ItemBoundsPortion.Label);
        _editBox.Font = item.Font;
        _editBox.Text = item.Text;
        _editBox.Visible = true;

        if (!Controls.Contains(_editBox)) Controls.Add(_editBox);

        _editBox.SelectAll();
        _editBox.Focus();
    }

    private ListViewLabelEditBox CreateEditBox()
    {
        var box = new ListViewLabelEditBox();
        box.KeyDown += OnEditBoxKeyDown;
        box.LostFocus += OnEditBoxLostFocus;

        return box;
    }

    private void OnEditBoxLostFocus(object? sender, EventArgs e) => EndEditCore(commit: true);

    /// <summary>
    /// _editBox is removed from Controls (not disposed) once an edit ends, so it can be reused for
    /// the next one; disposing the ListView itself is the one place nothing reuses it again, so it
    /// is disposed here, detaching its handlers first so it does not call back into a disposed list.
    /// </summary>
    private void DisposeEditBoxCore()
    {
        if (_editBox is null) return;

        _editBox.KeyDown -= OnEditBoxKeyDown;
        _editBox.LostFocus -= OnEditBoxLostFocus;
        _editBox.Dispose();
        _editBox = null;
    }

    private void OnEditBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            EndEditCore(commit: true);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            EndEditCore(commit: false);
            e.Handled = true;
        }
    }

    /// <summary>Closes the edit box: commit applies the typed text through AfterLabelEdit; cancel discards it.</summary>
    private void EndEditCore(bool commit)
    {
        if (_editingItem is null || _editBox is null) return;

        var item = _editingItem;
        var index = IndexOfItem(item);
        var newLabel = commit ? _editBox.Text : null;

        _editingItem = null;
        _editBox.Visible = false;
        Controls.Remove(_editBox);

        var args = new LabelEditEventArgs(index, newLabel);
        OnAfterLabelEdit(args);

        if (!args.CancelEdit && newLabel is not null) item.Text = newLabel;
    }

    /// <summary>
    /// Disposes _editBox: once an edit ends it is only removed from Controls (so the same box can
    /// be reused for the next edit), so nothing else ever disposes it on its own, and it would
    /// otherwise outlive the ListView that created it.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeEditBoxCore();
            DisposeStateImagesAndTip();
        }

        base.Dispose(disposing);
    }

    /// <summary>The TextBox LabelEdit hosts: no border of its own, since the item's own label area is the only frame it needs.</summary>
    private sealed class ListViewLabelEditBox : TextBox
    {
        public ListViewLabelEditBox()
        {
            BorderStyle = BorderStyle.None;
        }
    }
}
