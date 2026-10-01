using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// Editing the current cell with a plain TextBox placed over it (Microsoft's
/// IDataGridViewEditingControl/DataGridViewTextBoxEditingControl pair is left out this round,
/// so every cell type edits as text regardless of its own EditType), and sorting rows by one
/// column's value, with SortCompare able to override the comparison pair by pair.
/// </summary>
public partial class DataGridView
{
    private bool BeginEditCore(bool selectAll)
    {
        if (_currentCell is not { } cell) return false;
        if (EditingControl is not null) return true;
        if (ReadOnly || cell.ReadOnly) return false;

        var cancel = new DataGridViewCellCancelEventArgs(cell.ColumnIndex, cell.RowIndex);
        OnCellBeginEdit(cancel);
        if (cancel.Cancel) return false;

        var textBox = new TextBox();
        textBox.TextChanged += EditingControlTextChanged;
        textBox.KeyDown += EditingControlKeyDown;
        Controls.Add(textBox);
        EditingControl = textBox;

        var cellStyle = cell.InheritedStyle;
        cell.InitializeEditingControl(cell.RowIndex, cell.GetEditedFormattedValue(cell.RowIndex, DataGridViewDataErrorContexts.Formatting), cellStyle);

        PositionEditingControlCore();
        textBox.BringToFront();
        textBox.Visible = true;
        textBox.Focus();

        if (selectAll) textBox.SelectAll();
        else textBox.SelectionStart = textBox.Text.Length;

        IsCurrentCellDirty = false;

        return true;
    }

    private bool EndEditCore(DataGridViewDataErrorContexts context)
    {
        if (EditingControl is not TextBox textBox || _currentCell is not { } cell) return true;

        var validating = new DataGridViewCellValidatingEventArgs(cell.ColumnIndex, cell.RowIndex, textBox.Text);
        OnCellValidating(validating);
        if (validating.Cancel) return false;

        cell.Value = cell.ParseFormattedValue(textBox.Text, cell.InheritedStyle, null, null);

        RemoveEditingControl();

        OnCellValidated(new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex));
        OnCellEndEdit(new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex));

        return true;
    }

    private bool CancelEditCore()
    {
        if (EditingControl is null || _currentCell is not { } current) return true;

        RemoveEditingControl();
        OnCellEndEdit(new DataGridViewCellEventArgs(current.ColumnIndex, current.RowIndex));

        return true;
    }

    /// <summary>
    /// Keeps the editing control glued to its cell: called right after it is created, and again
    /// whenever the grid itself is resized or any column/row changes size (including ones before
    /// the current cell, which shift where it sits) - through the cell's own PositionEditingControl
    /// rather than poking EditingControl.Bounds directly, so a cell type that overrides it keeps
    /// control of its own editor's placement.
    /// </summary>
    private void PositionEditingControlCore()
    {
        if (EditingControl is null || _currentCell is not { } cell) return;

        var bounds = GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, cutOverflow: false);
        cell.PositionEditingControl(true, true, bounds, bounds, cell.InheritedStyle, false, false, false, false);
    }

    /// <summary>Scrolling is not implemented this round (DataGridView.Layout.cs), so only the grid's own resize and column/row size changes need to reposition the editor.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionEditingControlCore();
    }

    private void RemoveEditingControl()
    {
        if (EditingControl is not TextBox textBox) return;

        textBox.TextChanged -= EditingControlTextChanged;
        textBox.KeyDown -= EditingControlKeyDown;
        Controls.Remove(textBox);
        textBox.Dispose();
        EditingControl = null;
        IsCurrentCellDirty = false;
        Focus();
    }

    /// <summary>
    /// The embedded TextBox, not the grid, holds the actual input focus while editing: Enter and
    /// Escape must be read here and forwarded, since they never reach DataGridView.OnKeyDown.
    /// </summary>
    private void EditingControlKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                EndEdit();
                e.Handled = true;
                break;

            case Keys.Escape:
                CancelEdit();
                e.Handled = true;
                break;
        }
    }

    private void EditingControlTextChanged(object? sender, EventArgs e)
    {
        if (IsCurrentCellDirty) return;

        IsCurrentCellDirty = true;
        OnCurrentCellDirtyStateChanged(EventArgs.Empty);
    }

    protected virtual void OnCurrentCellDirtyStateChanged(EventArgs e) => ((EventHandler?)Events[s_currentCellDirtyStateChangedEvent])?.Invoke(this, e);

    public event EventHandler? CurrentCellDirtyStateChanged
    {
        add => Events.AddHandler(s_currentCellDirtyStateChangedEvent, value);
        remove => Events.RemoveHandler(s_currentCellDirtyStateChangedEvent, value);
    }

    private static readonly object s_currentCellDirtyStateChangedEvent = new();

    private void SortCore(DataGridViewColumn dataGridViewColumn, ListSortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewColumn);
        CommitEditBeforeSort();

        var ordered = RowsSortedBy((a, b) => CompareColumn(dataGridViewColumn, a, b));
        if (direction == ListSortDirection.Descending) ordered.Reverse();

        Rows.ReorderCore(ordered);

        SortedColumn = dataGridViewColumn;
        SortOrder = direction == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending;

        OnSorted(EventArgs.Empty);
        Invalidate();
    }

    private void SortCore(IComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        CommitEditBeforeSort();

        var ordered = RowsSortedBy((a, b) => comparer.Compare(a, b));

        Rows.ReorderCore(ordered);
        SortedColumn = null;
        SortOrder = SortOrder.None;

        OnSorted(EventArgs.Empty);
        Invalidate();
    }

    /// <summary>
    /// Microsoft's documented Sort behaviour: a pending edit is committed first; when it cannot
    /// be committed (CellValidating cancels it) sorting itself is refused with the same exception
    /// Sort documents, rather than silently sorting around a dirty, uncommitted cell.
    /// </summary>
    private void CommitEditBeforeSort()
    {
        if (IsCurrentCellInEditMode && !EndEditCore(DataGridViewDataErrorContexts.Commit))
        {
            throw new InvalidOperationException("The current cell could not be committed to a new value before the DataGridView could be sorted.");
        }
    }

    private List<DataGridViewRow> RowsSortedBy(Comparison<DataGridViewRow> comparison)
    {
        var rows = new List<DataGridViewRow>(Rows.Cast<DataGridViewRow>());
        rows.Sort(comparison);

        return rows;
    }

    /// <summary>Compares two rows by one column's cell value: SortCompare can override the result when it has a handler.</summary>
    private int CompareColumn(DataGridViewColumn column, DataGridViewRow a, DataGridViewRow b)
    {
        var valueA = a.Cells[column.Index].Value;
        var valueB = b.Cells[column.Index].Value;

        var args = new DataGridViewSortCompareEventArgs(column, valueA, valueB, a.Index, b.Index);
        OnSortCompare(args);
        if (args.Handled) return args.SortResult;

        if (valueA is IComparable comparable) return comparable.CompareTo(valueB);

        return string.Compare(valueA?.ToString(), valueB?.ToString(), StringComparison.CurrentCulture);
    }
}
