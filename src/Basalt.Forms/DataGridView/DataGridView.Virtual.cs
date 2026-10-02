using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// The virtual-mode seam that can be wired without touching the cell/row classes themselves:
/// VirtualMode itself, CellErrorTextNeeded (consulted from DataGridView.Painting.cs instead of a
/// cell's own ErrorText once the grid is virtual), and the row-dirty-state pair
/// (IsCurrentRowDirty/RowDirtyStateNeeded/CancelRowEdit) a virtual-mode host uses to track and
/// confirm edits it keeps in its own store rather than in the grid's cells.
/// </summary>
/// <remarks>
/// Left out this round: CellValueNeeded/CellValuePushed (and, depending on the same placeholder
/// row this grid does not yet have, NewRowNeeded/DefaultValuesNeeded) need
/// DataGridViewCell.GetValue(int)/GetFormattedValueCore to consult the grid instead of the cell's
/// own backing field - both are called by the cell's own (non-virtual) GetFormattedValueCore
/// directly, bypassing the public Value property, so a DataGridView-side override cannot reach
/// them without a change to DataGridViewCell.cs, outside this round's files.
/// CellToolTipTextNeeded/CellContextMenuStripNeeded/RowContextMenuStripNeeded/
/// RowHeightInfoNeeded/RowHeightInfoPushed have the same shape of gap, plus no tooltip/
/// context-menu/row-height-query infrastructure yet calls them at all.
/// </remarks>
public partial class DataGridView
{
    private bool _virtualMode;

    [DefaultValue(false)]
    public bool VirtualMode
    {
        get => _virtualMode;
        set => _virtualMode = value;
    }

    [Browsable(false)]
    public bool IsCurrentRowDirty
    {
        get
        {
            if (!VirtualMode) return IsCurrentCellDirty;

            var args = new QuestionEventArgs(IsCurrentCellDirty);
            OnRowDirtyStateNeeded(args);

            return args.Response;
        }
    }

    public event DataGridViewCellErrorTextNeededEventHandler? CellErrorTextNeeded
    {
        add => Events.AddHandler(s_cellErrorTextNeededEvent, value);
        remove => Events.RemoveHandler(s_cellErrorTextNeededEvent, value);
    }

    public event QuestionEventHandler? RowDirtyStateNeeded
    {
        add => Events.AddHandler(s_rowDirtyStateNeededEvent, value);
        remove => Events.RemoveHandler(s_rowDirtyStateNeededEvent, value);
    }

    public event QuestionEventHandler? CancelRowEdit
    {
        add => Events.AddHandler(s_cancelRowEditEvent, value);
        remove => Events.RemoveHandler(s_cancelRowEditEvent, value);
    }

    private static readonly object s_cellErrorTextNeededEvent = new();
    private static readonly object s_rowDirtyStateNeededEvent = new();
    private static readonly object s_cancelRowEditEvent = new();

    protected virtual void OnCellErrorTextNeeded(DataGridViewCellErrorTextNeededEventArgs e) => ((DataGridViewCellErrorTextNeededEventHandler?)Events[s_cellErrorTextNeededEvent])?.Invoke(this, e);

    protected virtual void OnRowDirtyStateNeeded(QuestionEventArgs e) => ((QuestionEventHandler?)Events[s_rowDirtyStateNeededEvent])?.Invoke(this, e);

    protected virtual void OnCancelRowEdit(QuestionEventArgs e) => ((QuestionEventHandler?)Events[s_cancelRowEditEvent])?.Invoke(this, e);

    /// <summary>Called from DataGridView.Painting.cs instead of reading cell.ErrorText directly, so a virtual-mode host can supply it on demand instead of storing it on the cell.</summary>
    internal string? GetCellErrorTextCore(DataGridViewCell cell, int rowIndex)
    {
        if (!VirtualMode) return cell.ErrorText.Length == 0 ? null : cell.ErrorText;

        var args = new DataGridViewCellErrorTextNeededEventArgs(cell.ColumnIndex, rowIndex, cell.ErrorText);
        OnCellErrorTextNeeded(args);

        return string.IsNullOrEmpty(args.ErrorText) ? null : args.ErrorText;
    }

    /// <summary>
    /// Asks CancelRowEdit before actually cancelling a dirty row's edit in virtual mode, called
    /// from DataGridView.Edit.cs's CancelEditCore: a handler that sets Response to false keeps the
    /// edit (and the editing control) open, exactly as CancelEdit already refuses when EndEdit's
    /// own validation cancels a commit.
    /// </summary>
    internal bool ConfirmCancelRowEditCore()
    {
        if (!VirtualMode || !IsCurrentRowDirty) return true;

        var args = new QuestionEventArgs(true);
        OnCancelRowEdit(args);

        return args.Response;
    }
}
