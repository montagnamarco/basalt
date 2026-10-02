using System.ComponentModel;
using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// Clicking a value cell opens a borderless TextBox over it; Enter commits
/// through the property's TypeConverter, Escape cancels, and an invalid
/// string leaves the old value in place and never raises
/// PropertyValueChanged. Clicking the +/- glyph toggles a row; dragging the
/// line between the name and value columns moves the splitter.
/// </summary>
/// <remarks>
/// Enum and bool properties (anything whose TypeConverter offers standard
/// values) are not given a drop-down: double-clicking their value cycles to
/// the next standard value instead, the simpler of the two choices the
/// family's contract allows.
/// </remarks>
public partial class PropertyGrid
{
    private TextBox? _editor;
    private int _editingRowIndex = -1;
    private PropertyGridEntry? _editingEntry;

    /// <summary>The row index whose value is covered by the in-place editor, or -1; painting skips that cell's text while it is up.</summary>
    internal int EditingRowIndex => _editingRowIndex;

    /// <summary>The in-place TextBox itself, for tests; null whenever no cell is being edited.</summary>
    internal TextBox? Editor => _editor is { Visible: true } ? _editor : null;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (HandleToolbarClick(e.Location)) return;

        if (IsOverSplitter(e.Location))
        {
            _draggingSplitter = true;
            Capture = true;
            return;
        }

        var rowIndex = RowIndexAt(e.Location);
        if (rowIndex < 0)
        {
            CancelEdit();
            return;
        }

        var rows = VisibleRowsWithDepth();
        var (item, depth) = rows[rowIndex];

        if (item.Expandable && PlusMinusBounds(rowIndex, depth).Contains(e.Location))
        {
            item.Expanded = !item.Expanded;
            CancelEdit();
            PerformLayout();
            Invalidate();
            return;
        }

        item.Select();

        if (item is PropertyGridEntry propertyEntry && ValueBounds(rowIndex).Contains(e.Location))
        {
            if (e.Clicks >= 2 && TryCycleStandardValue(propertyEntry))
            {
                Invalidate();
                return;
            }

            BeginEdit(rowIndex, propertyEntry);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_draggingSplitter)
        {
            _labelColumnWidth = e.X - GridBounds.Left;
            PositionEditor();
            Invalidate();
            return;
        }

        Cursor = IsOverSplitter(e.Location) ? Cursors.VSplit : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_draggingSplitter)
        {
            _draggingSplitter = false;
            Capture = false;
        }
    }

    /// <summary>Up/Down move the selection among the rows on screen; Left collapses an expanded row or selects its parent, Right expands it or selects its first child.</summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Up: return MoveSelection(-1);
            case Keys.Down: return MoveSelection(1);
            case Keys.Left: return CollapseOrSelectParent();
            case Keys.Right: return ExpandOrSelectFirstChild();
        }

        return base.ProcessDialogKey(keyData);
    }

    private bool MoveSelection(int delta)
    {
        var rows = VisibleRowsWithDepth();
        if (rows.Count == 0) return false;

        var current = SelectedGridItem;
        var index = current is null ? -1 : rows.FindIndex(r => ReferenceEquals(r.Item, current));
        var next = Math.Clamp(index + delta, 0, rows.Count - 1);

        rows[next].Item.Select();
        return true;
    }

    private bool CollapseOrSelectParent()
    {
        if (SelectedGridItem is not { } item) return false;

        if (item.Expandable && item.Expanded)
        {
            item.Expanded = false;
            PerformLayout();
            Invalidate();
            return true;
        }

        if (item.Parent is { GridItemType: not GridItemType.Root } parent)
        {
            parent.Select();
            return true;
        }

        return false;
    }

    private bool ExpandOrSelectFirstChild()
    {
        if (SelectedGridItem is not { } item || !item.Expandable) return false;

        if (!item.Expanded)
        {
            item.Expanded = true;
            PerformLayout();
            Invalidate();
            return true;
        }

        if (item.GridItems.Count > 0)
        {
            item.GridItems[0].Select();
            return true;
        }

        return false;
    }

    /// <summary>Cycles to the next of the property's TypeConverter.GetStandardValues (bool and enum properties typically), wrapping past the last.</summary>
    private bool TryCycleStandardValue(PropertyGridEntry entry)
    {
        var descriptor = entry.PropertyDescriptor;
        if (descriptor is null || descriptor.IsReadOnly || !descriptor.Converter.GetStandardValuesSupported()) return false;

        var values = descriptor.Converter.GetStandardValues();
        if (values is null || values.Count == 0) return false;

        var list = values.Cast<object?>().ToList();
        var index = list.FindIndex(v => Equals(v, entry.Value));
        var next = list[(index + 1) % list.Count];

        CommitValue(entry, next);
        return true;
    }

    private void BeginEdit(int rowIndex, PropertyGridEntry entry)
    {
        if (entry.PropertyDescriptor?.IsReadOnly == true) return;

        CancelEdit();

        _editor ??= CreateEditor();
        _editingRowIndex = rowIndex;
        _editingEntry = entry;

        _editor.Text = ValueText(entry);
        if (!Controls.Contains(_editor)) Controls.Add(_editor);

        PositionEditor();
        _editor.Visible = true;
        _editor.BringToFront();
        _editor.Focus();
        _editor.SelectAll();

        Invalidate();
    }

    private TextBox CreateEditor()
    {
        var editor = new TextBox { BorderStyle = BorderStyle.None, Visible = false };
        editor.KeyDown += OnEditorKeyDown;

        return editor;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                CommitEdit();
                e.Handled = true;
                break;

            case Keys.Escape:
                CancelEdit();
                e.Handled = true;
                break;
        }
    }

    private void PositionEditor()
    {
        if (_editor is null || _editingRowIndex < 0) return;

        var bounds = ValueBounds(_editingRowIndex);
        _editor.Bounds = new Rectangle(bounds.Left + 1, bounds.Top + 1, Math.Max(bounds.Width - 2, 0), Math.Max(bounds.Height - 2, 0));
    }

    /// <summary>Converts the editor's text through the property's TypeConverter; an invalid string is caught and leaves the old value, raising nothing.</summary>
    private void CommitEdit()
    {
        if (_editingEntry is not { } entry)
        {
            EndEdit();
            return;
        }

        var descriptor = entry.PropertyDescriptor!;
        var text = _editor!.Text;

        // An invalid string, or text for a property no converter can fill from
        // text, keeps the old value and raises no PropertyValueChanged.
        if (TryConvertFromText(descriptor, text, out var newValue)) CommitValue(entry, newValue);

        EndEdit();
    }

    private static bool TryConvertFromText(PropertyDescriptor descriptor, string text, out object? value)
    {
        value = null;

        if (!descriptor.Converter.CanConvertFrom(typeof(string)))
        {
            if (!descriptor.PropertyType.IsAssignableFrom(typeof(string))) return false;

            value = text;
            return true;
        }

        try
        {
            value = descriptor.Converter.ConvertFromString(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void CommitValue(PropertyGridEntry entry, object? newValue)
    {
        var oldValue = entry.Value;
        if (Equals(oldValue, newValue)) return;

        // A setter that refuses the value leaves the property as it was, with no event.
        if (!entry.TryApplyValue(newValue)) return;

        Invalidate();
        OnPropertyValueChanged(new PropertyValueChangedEventArgs(entry, oldValue));
    }

    internal void CancelEdit() => EndEdit();

    private void EndEdit()
    {
        if (_editor is { Visible: true }) _editor.Visible = false;

        _editingRowIndex = -1;
        _editingEntry = null;
        Invalidate();
    }

    private void DisposeEditor()
    {
        if (_editor is null) return;

        _editor.KeyDown -= OnEditorKeyDown;
        _editor.Dispose();
        _editor = null;
    }
}
