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

    private bool _beginningEdit;

    private bool CurrentCellIsAttached(DataGridViewCell cell) => !IsDisposed && !Disposing &&
        ReferenceEquals(_currentCell, cell) && ReferenceEquals(cell.DataGridView, this) && cell.RowIndex >= 0 && cell.ColumnIndex >= 0;

    private bool EditingCellIsAttached(DataGridViewCell cell, Control control) =>
        CurrentCellIsAttached(cell) && ReferenceEquals(EditingControl, control) && !control.IsDisposed;

    private bool BeginEditCore(bool selectAll)
    {
        if (_beginningEdit || _currentCell is not { } cell || !CurrentCellIsAttached(cell)) return false;
        if (EditingControl is not null) return true;
        if (ReadOnly || cell.ReadOnly || !cell.UsesTextEditorInternal) return false;

        _beginningEdit = true;
        Control? control = null;
        try
        {
            var cancel = new DataGridViewCellCancelEventArgs(cell.ColumnIndex, cell.RowIndex);
            OnCellBeginEdit(cancel);
            if (cancel.Cancel || !CurrentCellIsAttached(cell)) return false;

            control = ResolveEditingControl(cell.EditType ?? typeof(DataGridViewTextBoxEditingControl));
            var editingControl = control as IDataGridViewEditingControl;
            EditingControl = control;
            Controls.Add(EditingPanel);
            EditingPanel.Controls.Add(control);
            EditingPanel.Visible = true;
            var cellStyle = cell.InheritedStyle;

            // Preparing the pooled editor is not a user edit. Callbacks may remove its cell,
            // cancel editing or dispose the grid, so each return boundary checks ownership.
            _preparingEditingControl = true;
            if (editingControl is not null)
            {
                editingControl.EditingControlDataGridView = this;
                editingControl.EditingControlRowIndex = cell.RowIndex;
                editingControl.ApplyCellStyleToEditingControl(cellStyle);
            }
            if (!EditingCellIsAttached(cell, control)) return false;
            var value = cell.GetEditedFormattedValue(cell.RowIndex, DataGridViewDataErrorContexts.Formatting);
            if (!EditingCellIsAttached(cell, control)) return false;
            cell.InitializeEditingControl(cell.RowIndex, value, cellStyle);
            if (!EditingCellIsAttached(cell, control)) return false;

            var showing = new DataGridViewEditingControlShowingEventArgs(control, cellStyle);
            OnEditingControlShowing(showing);
            if (!EditingCellIsAttached(cell, control)) return false;
            editingControl?.ApplyCellStyleToEditingControl(showing.CellStyle);
            if (!EditingCellIsAttached(cell, control)) return false;

            PositionEditingControlCore();
            if (!EditingCellIsAttached(cell, control)) return false;
            EditingPanel.BringToFront();
            control.BringToFront();
            control.Visible = true;
            control.KeyDown += EditingControlKeyDown;
            control.Focus();
            if (!EditingCellIsAttached(cell, control)) return false;
            editingControl?.PrepareEditingControlForEdit(selectAll);
            if (!EditingCellIsAttached(cell, control)) return false;
            IsCurrentCellDirty = false;
            return true;
        }
        catch
        {
            if (control is not null && ReferenceEquals(EditingControl, control) && !IsDisposed) RemoveEditingControl();
            throw;
        }
        finally
        {
            _preparingEditingControl = false;
            _beginningEdit = false;
            if (control is not null && ReferenceEquals(EditingControl, control) && !EditingCellIsAttached(cell, control) && !IsDisposed)
                RemoveEditingControl();
        }
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
        if (EditingControl is null || _currentCell is not { } cell) return true;
        if (!CommitCurrentCellValue(context)) return false;

        RemoveEditingControl();

        OnCellValidated(new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex));
        OnCellEndEdit(new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex));

        return true;
    }

    /// <summary>Commits the editor's value while leaving the editor in place, as CommitEdit requires.</summary>
    private bool _committingCell;

    private bool CommitCurrentCellValue(DataGridViewDataErrorContexts context)
    {
        if (_committingCell) return false;
        _committingCell = true;
        try { return CommitCurrentCellValueCore(context); }
        finally { _committingCell = false; }
    }

    private bool CommitCurrentCellValueCore(DataGridViewDataErrorContexts context)
    {
        if (EditingControl is not IDataGridViewEditingControl editingControl || _currentCell is not { } cell) return true;
        var control = EditingControl;

        var formattedValue = editingControl.GetEditingControlFormattedValue(context);
        if (!EditingCellIsAttached(cell, control)) return false;
        var validating = new DataGridViewCellValidatingEventArgs(cell.ColumnIndex, cell.RowIndex, formattedValue);
        OnCellValidating(validating);
        if (validating.Cancel || !EditingCellIsAttached(cell, control)) return false;

        var previousValue = cell.Value;
        var parsedValue = cell.ParseFormattedValue(formattedValue, cell.InheritedStyle, null, null);
        if (!EditingCellIsAttached(cell, control)) return false;
        cell.Value = parsedValue;
        if (!EditingCellIsAttached(cell, control)) return false;

        // A refused bound write must leave the original cell value available while the editor
        // remains open for correction, just as a refused CellValidating does.
        if (!PushCellValueToDataSource(cell))
        {
            if (EditingCellIsAttached(cell, control)) cell.Value = previousValue;
            return false;
        }

        if (!EditingCellIsAttached(cell, control)) return false;
        editingControl.EditingControlValueChanged = false;
        NotifyCurrentCellDirty(false);
        return EditingCellIsAttached(cell, control);
    }

    private bool CancelEditCore()
    {
        if (EditingControl is null || _currentCell is not { } current) return true;
        if (!ConfirmCancelRowEditCore()) return false;

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
        if (EditingControl is not { } control || _currentCell is not { } cell || !EditingCellIsAttached(cell, control)) return;

        var bounds = GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, cutOverflow: false);
        var panelBounds = cell.PositionEditingPanel(bounds, GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, true), cell.InheritedStyle, false, false, false, false);
        if (!EditingCellIsAttached(cell, control)) return;
        EditingPanel.Bounds = panelBounds;
        cell.PositionEditingControl(true, true, bounds, bounds, cell.InheritedStyle, false, false, false, false);
        if (!EditingCellIsAttached(cell, control)) return;
        control.Location = new System.Drawing.Point(control.Left - EditingPanel.Left, control.Top - EditingPanel.Top);
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
        EditingPanel.Controls.Remove(control);
        EditingPanel.Visible = false;
        EditingControl = null;
        IsCurrentCellDirty = false;
        if (!IsDisposed && !Disposing) Focus();
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
            var editors = _editingControlsByType.Values.ToArray();
            _editingControlsByType.Clear();
            foreach (var control in editors) control.Dispose();
            _cellToolTip?.Dispose();
            _editingPanel?.Dispose();
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
