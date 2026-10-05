using System.ComponentModel;
using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

public partial class DataGridView
{
    private bool _cellKeyWasDispatched;

    [DefaultValue(false)]
    public bool StandardTab { get; set; }

    protected virtual bool ProcessDataGridViewKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.KeyCode switch
        {
            Keys.A => ProcessAKey(e.KeyData),
            Keys.Up => ProcessUpKey(e.KeyData),
            Keys.Down => ProcessDownKey(e.KeyData),
            Keys.Left => ProcessLeftKey(e.KeyData),
            Keys.Right => ProcessRightKey(e.KeyData),
            Keys.Home => ProcessHomeKey(e.KeyData),
            Keys.End => ProcessEndKey(e.KeyData),
            Keys.PageUp => ProcessPriorKey(e.KeyData),
            Keys.PageDown => ProcessNextKey(e.KeyData),
            Keys.Enter => ProcessEnterKey(e.KeyData),
            Keys.Escape => ProcessEscapeKey(e.KeyData),
            Keys.F2 => ProcessF2Key(e.KeyData),
            Keys.F3 => ProcessF3Key(e.KeyData),
            Keys.Delete => ProcessDeleteKey(e.KeyData),
            Keys.Insert => ProcessInsertKey(e.KeyData),
            Keys.Space => ProcessSpaceKey(e.KeyData),
            Keys.Tab => ProcessTabKey(e.KeyData),
            Keys.F10 => ProcessControlShiftF10Keys(e.KeyData),
            Keys.D0 => ProcessZeroKey(e.KeyData),
            _ => false,
        };
    }

    protected override bool ProcessDialogKey(Keys keyData) =>
        ProcessDataGridViewKey(new KeyEventArgs(keyData)) || base.ProcessDialogKey(keyData);

    protected override bool ProcessKeyEventArgs(ref Message m) => base.ProcessKeyEventArgs(ref m);
    protected override bool ProcessKeyPreview(ref Message m) => base.ProcessKeyPreview(ref m);

    protected bool ProcessAKey(Keys keyData)
    {
        if ((keyData & Keys.Modifiers) != Keys.Control || !MultiSelect) return false;
        SelectAll();
        return true;
    }

    protected bool ProcessUpKey(Keys keyData) => MoveByKey(0, -1, keyData);
    protected bool ProcessDownKey(Keys keyData) => MoveByKey(0, 1, keyData);
    protected bool ProcessLeftKey(Keys keyData) => MoveByKey(-1, 0, keyData);
    protected bool ProcessRightKey(Keys keyData) => MoveByKey(1, 0, keyData);
    protected bool ProcessPriorKey(Keys keyData) => MoveByKey(0, -Math.Max(1, DisplayedRowCount(false)), keyData);
    protected bool ProcessNextKey(Keys keyData) => MoveByKey(0, Math.Max(1, DisplayedRowCount(false)), keyData);

    private bool MoveByKey(int columns, int rows, Keys keyData)
    {
        if ((keyData & Keys.Alt) != 0) return false;
        MoveCurrentCell(columns, rows, (keyData & Keys.Shift) != 0);
        return true;
    }

    protected bool ProcessHomeKey(Keys keyData)
    {
        if ((keyData & Keys.Alt) != 0) return false;
        MoveToEdge(false, keyData);
        return true;
    }

    protected bool ProcessEndKey(Keys keyData)
    {
        if ((keyData & Keys.Alt) != 0) return false;
        MoveToEdge(true, keyData);
        return true;
    }

    private void MoveToEdge(bool end, Keys keyData)
    {
        var columns = ColumnsInDisplayOrder().Where(column => column.Visible).ToArray();
        var rows = Rows.Cast<DataGridViewRow>().Where(row => row.Visible).ToArray();
        if (columns.Length == 0 || rows.Length == 0) return;
        var column = end ? columns[^1] : columns[0];
        var row = (keyData & Keys.Control) != 0 ? (end ? rows[^1] : rows[0])
            : rows.FirstOrDefault(candidate => candidate.Index == CurrentCellAddress.Y) ?? rows[0];
        MoveCurrentCellTo(column.Index, row.Index, (keyData & Keys.Shift) != 0);
    }

    protected bool ProcessEnterKey(Keys keyData)
    {
        if ((keyData & Keys.Alt) != 0) return false;
        if (IsCurrentCellInEditMode) EndEdit();
        else MoveCurrentCell(0, (keyData & Keys.Shift) != 0 ? -1 : 1, false);
        return true;
    }

    protected bool ProcessEscapeKey(Keys keyData)
    {
        if (!IsCurrentCellInEditMode) return false;
        CancelEdit();
        return true;
    }

    protected bool ProcessF2Key(Keys keyData) => (keyData & Keys.Modifiers) == Keys.None && BeginEdit(false);

    protected bool ProcessF3Key(Keys keyData)
    {
        if ((keyData & Keys.Modifiers) != Keys.None || CurrentCell?.OwningColumn is not { SortMode: DataGridViewColumnSortMode.Automatic } column) return false;
        var direction = SortedColumn == column && SortOrder == SortOrder.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending;
        Sort(column, direction);
        column.HeaderCell.SortGlyphDirection = direction == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending;
        return true;
    }

    protected bool ProcessTabKey(Keys keyData)
    {
        if (StandardTab || (keyData & (Keys.Control | Keys.Alt)) != 0 || CurrentCell is null) return false;
        var cells = Rows.Cast<DataGridViewRow>().Where(row => row.Visible)
            .SelectMany(row => ColumnsInDisplayOrder().Where(column => column.Visible).Select(column => row.Cells[column.Index])).ToArray();
        var next = Array.IndexOf(cells, CurrentCell) + ((keyData & Keys.Shift) != 0 ? -1 : 1);
        if (next < 0 || next >= cells.Length) return false;
        MoveCurrentCellTo(cells[next].ColumnIndex, cells[next].RowIndex, false);
        return true;
    }

    protected bool ProcessControlShiftF10Keys(Keys keyData)
    {
        if ((keyData & Keys.Modifiers) != (Keys.Control | Keys.Shift) || CurrentCell is not { } cell) return false;
        var menu = cell.GetInheritedContextMenuStrip(cell.RowIndex);
        if (menu is null) return false;
        var bounds = GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, true);
        menu.Show(this, new Point(bounds.Left, bounds.Bottom));
        return true;
    }

    protected bool ProcessSpaceKey(Keys keyData)
    {
        if (_cellKeyWasDispatched || CurrentCell is not { } cell) return false;
        var args = new KeyEventArgs(keyData);
        cell.OnKeyDownCore(args, cell.RowIndex);
        return args.Handled;
    }

    // Insert has no grid command: clipboard shortcuts remain available to the parent.
    protected bool ProcessInsertKey(Keys keyData) => false;

    protected bool ProcessZeroKey(Keys keyData)
    {
        if ((keyData & Keys.Modifiers) != Keys.Control || CurrentCell is not { } cell || ReadOnly || cell.ReadOnly) return false;
        if (IsCurrentCellInEditMode && !EndEdit()) return true;
        cell.Value = cell.InheritedStyle.DataSourceNullValue;
        return true;
    }

    protected bool ProcessDeleteKey(Keys keyData)
    {
        if ((keyData & Keys.Modifiers) != Keys.None || !AllowUserToDeleteRows || ReadOnly) return false;
        var rows = SelectedRows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).OrderByDescending(row => row.Index).ToArray();
        if (rows.Length == 0) return false;
        if (IsCurrentCellInEditMode && !EndEdit()) return true;
        foreach (var row in rows)
        {
            if (IsDisposed || Disposing) break;
            if (row.DataGridView != this) continue;
            var args = new DataGridViewRowCancelEventArgs(row);
            OnUserDeletingRow(args);
            if (IsDisposed || Disposing) break;
            if (args.Cancel || row.DataGridView != this) continue;
            if (_currencyManager is { } manager)
            {
                if (manager.List.IsReadOnly || manager.List.IsFixedSize || manager.List is IBindingList { AllowRemove: false }) continue;
                manager.List.RemoveAt(row.Index);
            }
            else Rows.RemoveAt(row.Index);
            OnUserDeletedRow(new DataGridViewRowEventArgs(row));
        }
        return true;
    }
}
