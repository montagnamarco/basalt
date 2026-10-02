using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// Editing the current cell with a control created from the cell's own EditType - a
/// DataGridViewTextBoxEditingControl for a text cell, a DataGridViewComboBoxEditingControl for a
/// combo cell, and so on - one instance kept per control type and reused for every cell edited
/// with it afterwards, exactly as Microsoft's grid pools its editing controls. Also sorts rows by
/// one column's value, with SortCompare able to override the comparison pair by pair.
/// </summary>
public partial class DataGridView
{
    /// <summary>One editing control instance per EditType, kept alive and reused rather than recreated for every cell.</summary>
    private readonly Dictionary<Type, Control> _editingControlsByType = new();

    private bool BeginEditCore(bool selectAll)
    {
        if (_currentCell is not { } cell) return false;
        if (EditingControl is not null) return true;
        if (ReadOnly || cell.ReadOnly) return false;
        if (!cell.UsesTextEditorInternal) return false;

        var cancel = new DataGridViewCellCancelEventArgs(cell.ColumnIndex, cell.RowIndex);
        OnCellBeginEdit(cancel);
        if (cancel.Cancel) return false;

        var editingControlType = cell.EditType ?? typeof(DataGridViewTextBoxEditingControl);
        var control = ResolveEditingControl(editingControlType);
        var editingControl = control as IDataGridViewEditingControl;

        EditingControl = control;
        Controls.Add(control);

        var cellStyle = cell.InheritedStyle;

        // Loading the cell's value into the editor is not an edit: changes the editor reports
        // until it is ready are ignored, so CurrentCellDirtyStateChanged fires for the user's
        // first change only.
        _preparingEditingControl = true;
        try
        {
            if (editingControl is not null)
            {
                editingControl.EditingControlDataGridView = this;
                editingControl.EditingControlRowIndex = cell.RowIndex;
                editingControl.ApplyCellStyleToEditingControl(cellStyle);
            }

            cell.InitializeEditingControl(cell.RowIndex, cell.GetEditedFormattedValue(cell.RowIndex, DataGridViewDataErrorContexts.Formatting), cellStyle);

            var showing = new DataGridViewEditingControlShowingEventArgs(control, cellStyle);
            OnEditingControlShowing(showing);
            editingControl?.ApplyCellStyleToEditingControl(showing.CellStyle);

            PositionEditingControlCore();
            control.BringToFront();
            control.Visible = true;
            control.KeyDown += EditingControlKeyDown;
            control.Focus();

            editingControl?.PrepareEditingControlForEdit(selectAll);
        }
        finally
        {
            _preparingEditingControl = false;
        }

        IsCurrentCellDirty = false;

        return true;
    }

    /// <summary>The pooled instance for editingControlType, creating and caching it the first time any cell needs that type.</summary>
    private Control ResolveEditingControl(Type editingControlType)
    {
        if (_editingControlsByType.TryGetValue(editingControlType, out var existing)) return existing;

        var created = (Control)Activator.CreateInstance(editingControlType)!;
        _editingControlsByType[editingControlType] = created;

        return created;
    }

    private bool EndEditCore(DataGridViewDataErrorContexts context)
    {
        if (EditingControl is not { } control || control is not IDataGridViewEditingControl editingControl || _currentCell is not { } cell) return true;

        var formattedValue = editingControl.GetEditingControlFormattedValue(context);

        var validating = new DataGridViewCellValidatingEventArgs(cell.ColumnIndex, cell.RowIndex, formattedValue);
        OnCellValidating(validating);
        if (validating.Cancel) return false;

        cell.Value = cell.ParseFormattedValue(formattedValue, cell.InheritedStyle, null, null);

        // A bound column writes the parsed value back into the data source here; a DataError
        // handler that cancels it keeps the cell in edit mode, exactly as a refused CellValidating does.
        if (!PushCellValueToDataSource(cell)) return false;

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

    /// <summary>
    /// Hides the pooled control again rather than disposing it (the next cell of the same
    /// EditType reuses the very same instance), and gives the cell a chance to detach anything it
    /// attached to it (DataGridViewComboBoxCell clears the dropdown list, for one).
    /// </summary>
    private void RemoveEditingControl()
    {
        if (EditingControl is not { } control) return;

        control.KeyDown -= EditingControlKeyDown;
        _currentCell?.DetachEditingControl();
        Controls.Remove(control);
        EditingControl = null;
        IsCurrentCellDirty = false;
        Focus();
    }

    /// <summary>
    /// The embedded editing control, not the grid, holds the actual input focus while editing:
    /// Enter and Escape must be read here and forwarded, since they never reach
    /// DataGridView.OnKeyDown while a child control owns the focus.
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

    /// <summary>
    /// What an editing control calls (through EditingControlDataGridView) to report that its
    /// value changed since PrepareEditingControlForEdit: raises CurrentCellDirtyStateChanged so a
    /// host watching it (for an icon, a Save button...) learns the cell is now dirty.
    /// </summary>
    /// <summary>True while BeginEdit fills the editing control with the cell's value.</summary>
    private bool _preparingEditingControl;

    public virtual void NotifyCurrentCellDirty(bool dirty)
    {
        if (_preparingEditingControl || dirty == IsCurrentCellDirty) return;

        IsCurrentCellDirty = dirty;
        OnCurrentCellDirtyStateChanged(EventArgs.Empty);
    }

    protected virtual void OnCurrentCellDirtyStateChanged(EventArgs e) => ((EventHandler?)Events[s_currentCellDirtyStateChangedEvent])?.Invoke(this, e);

    /// <summary>Raised right before the editing control just created is made visible, so a handler can still change its CellStyle or read/alter the control itself.</summary>
    protected virtual void OnEditingControlShowing(DataGridViewEditingControlShowingEventArgs e) =>
        ((DataGridViewEditingControlShowingEventHandler?)Events[s_editingControlShowingEvent])?.Invoke(this, e);

    public event EventHandler? CurrentCellDirtyStateChanged
    {
        add => Events.AddHandler(s_currentCellDirtyStateChangedEvent, value);
        remove => Events.RemoveHandler(s_currentCellDirtyStateChangedEvent, value);
    }

    public event DataGridViewEditingControlShowingEventHandler? EditingControlShowing
    {
        add => Events.AddHandler(s_editingControlShowingEvent, value);
        remove => Events.RemoveHandler(s_editingControlShowingEvent, value);
    }

    private static readonly object s_currentCellDirtyStateChangedEvent = new();
    private static readonly object s_editingControlShowingEvent = new();

    /// <summary>The pooled editing controls belong to the grid alone (they are never left in any cell's own Controls), so the grid disposes them itself.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var control in _editingControlsByType.Values) control.Dispose();
            _editingControlsByType.Clear();
        }

        base.Dispose(disposing);
    }

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
