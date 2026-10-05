using System.Drawing;

namespace System.Windows.Forms;

public partial class DataGridView
{
    public override bool AutoSize { get => base.AutoSize; set => base.AutoSize = value; }
    public override Color BackColor { get => base.BackColor; set => base.BackColor = value; }
    public override Image? BackgroundImage { get => base.BackgroundImage; set => base.BackgroundImage = value; }
    public override ImageLayout BackgroundImageLayout { get => base.BackgroundImageLayout; set => base.BackgroundImageLayout = value; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override Font Font { get => base.Font; set => base.Font = value; }
    public override Color ForeColor { get => base.ForeColor; set => base.ForeColor = value; }
    public new Padding Padding { get => base.Padding; set => base.Padding = value; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => base.Text; set => base.Text = value; }
    public override void ResetText() => base.ResetText();
    public new event EventHandler? StyleChanged { add => base.StyleChanged += value; remove => base.StyleChanged -= value; }
    protected override bool CanEnableIme => !ReadOnly && base.CanEnableIme;
    protected override CreateParams CreateParams => base.CreateParams;
    protected override Size DefaultSize => new(240, 150);
    protected override ControlCollection CreateControlsInstance() => new DataGridViewControlCollection(this);
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, height, specified);
    protected override void WndProc(ref Message m) => base.WndProc(ref m);

    public int RowCount
    {
        get => Rows.Count;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (IsDataBound) throw new InvalidOperationException("RowCount cannot be changed while the grid is data-bound.");
            if (value > 0 && Columns.Count == 0) Columns.Add(new DataGridViewTextBoxColumn());
            if (value > Rows.Count) Rows.Add(value - Rows.Count);
            else while (Rows.Count > value) Rows.RemoveAt(Rows.Count - 1);
        }
    }

    protected void ClearSelection(int columnIndexException, int rowIndexException, bool selectExceptionElement)
    {
        if (columnIndexException < -1 || columnIndexException >= Columns.Count) throw new ArgumentOutOfRangeException(nameof(columnIndexException));
        if (rowIndexException < -1 || rowIndexException >= Rows.Count) throw new ArgumentOutOfRangeException(nameof(rowIndexException));
        var selected = columnIndexException >= 0 && rowIndexException >= 0 && Rows[rowIndexException].Cells[columnIndexException].Selected;
        ClearSelectionQuiet();
        if (selectExceptionElement || selected)
        {
            if (columnIndexException == -1 && rowIndexException >= 0) SetSelectedRowCore(rowIndexException, true);
            else if (rowIndexException == -1 && columnIndexException >= 0) SetSelectedColumnCore(columnIndexException, true);
            else if (columnIndexException >= 0 && rowIndexException >= 0) SetSelectedCellCore(columnIndexException, rowIndexException, true);
        }
        OnSelectionChanged(EventArgs.Empty);
        Invalidate();
    }

    public void UpdateRowHeightInfo(int rowIndex, bool updateToEnd)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count) throw new ArgumentOutOfRangeException(nameof(rowIndex));
        for (var index = rowIndex; index < (updateToEnd ? Rows.Count : rowIndex + 1); index++)
        {
            var row = Rows[index];
            if (VirtualMode)
            {
                var height = GetRowHeightInfoCore(row);
                row.MinimumHeight = height.MinimumHeight;
                row.Height = height.Height;
            }
        }
        PerformLayout();
        Invalidate();
    }
}
