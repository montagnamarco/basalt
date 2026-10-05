using System.ComponentModel;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

public partial class DataGridView
{
    private DataGridViewClipboardCopyMode _clipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithAutoHeaderText;

    [DefaultValue(DataGridViewClipboardCopyMode.EnableWithAutoHeaderText)]
    public DataGridViewClipboardCopyMode ClipboardCopyMode
    {
        get => _clipboardCopyMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DataGridViewClipboardCopyMode));
            _clipboardCopyMode = value;
        }
    }

    public virtual DataObject? GetClipboardContent()
    {
        if (ClipboardCopyMode == DataGridViewClipboardCopyMode.Disable)
            throw new InvalidOperationException("Clipboard copying is disabled.");

        var selected = Rows.Cast<DataGridViewRow>().SelectMany(row => row.Cells.Cast<DataGridViewCell>())
            .Where(cell => cell.Selected && cell.Visible).ToArray();
        if (selected.Length == 0) return null;

        var firstRow = selected.Min(cell => cell.RowIndex);
        var lastRow = selected.Max(cell => cell.RowIndex);
        var firstColumn = selected.Min(cell => cell.OwningColumn!.DisplayIndex);
        var lastColumn = selected.Max(cell => cell.OwningColumn!.DisplayIndex);
        var columns = ColumnsInDisplayOrder().Where(column => column.Visible
            && column.DisplayIndex >= firstColumn && column.DisplayIndex <= lastColumn).ToArray();
        var includeColumns = ClipboardCopyMode == DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
            || ClipboardCopyMode == DataGridViewClipboardCopyMode.EnableWithAutoHeaderText && _selectedColumnIndexes.Count > 0;
        var includeRows = ClipboardCopyMode == DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
            || ClipboardCopyMode == DataGridViewClipboardCopyMode.EnableWithAutoHeaderText && _selectedRowIndexes.Count > 0;
        var rows = Enumerable.Range(firstRow, lastRow - firstRow + 1)
            .Select(index => Rows[index]).Where(row => row.Visible).ToArray();
        var text = BuildClipboardFormat(DataFormats.Text);
        var csv = BuildClipboardFormat(DataFormats.CommaSeparatedValue);
        var fragment = "<table>" + BuildClipboardFormat(DataFormats.Html) + "</table>";
        var data = new DataObject();
        data.SetData(DataFormats.Text, text);
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData(DataFormats.CommaSeparatedValue, csv);
        data.SetData(DataFormats.Html, HtmlClipboardFormat.WrapFragment(fragment));
        return data;

        string BuildClipboardFormat(string format)
        {
            var parts = new List<string>();
            var cellsPerRow = columns.Length + (includeRows ? 1 : 0);
            if (includeColumns)
            {
                for (var cellIndex = 0; cellIndex < cellsPerRow; cellIndex++)
                {
                    var headerCell = includeRows && cellIndex == 0 ? TopLeftHeaderCell
                        : columns[cellIndex - (includeRows ? 1 : 0)].HeaderCell;
                    parts.Add(headerCell.GetClipboardContentCore(-1, cellIndex == 0,
                        cellIndex == cellsPerRow - 1, inFirstRow: true, inLastRow: rows.Length == 0,
                        format: format)?.ToString() ?? string.Empty);
                }
            }

            for (var rowPosition = 0; rowPosition < rows.Length; rowPosition++)
            {
                var row = rows[rowPosition];
                var rowIndex = row.Index;
                var lastRowInRegion = rowPosition == rows.Length - 1;
                if (includeRows)
                {
                    parts.Add(row.HeaderCell.GetClipboardContentCore(rowIndex, firstCell: true,
                        lastCell: cellsPerRow == 1, inFirstRow: rowPosition == 0 && !includeColumns,
                        inLastRow: lastRowInRegion, format: format)?.ToString() ?? string.Empty);
                }

                for (var columnPosition = 0; columnPosition < columns.Length; columnPosition++)
                {
                    var cell = row.Cells[columns[columnPosition].Index];
                    var firstCell = columnPosition == 0 && !includeRows;
                    var lastCell = columnPosition == columns.Length - 1;
                    var content = cell.Selected
                        ? cell.GetClipboardContentCore(rowIndex, firstCell, lastCell,
                            inFirstRow: rowPosition == 0 && !includeColumns, inLastRow: lastRowInRegion, format: format)
                        : DataGridViewCell.FormatClipboardText(string.Empty, firstCell, lastCell,
                            lastRowInRegion, format);
                    parts.Add(content?.ToString() ?? string.Empty);
                }
            }

            return string.Concat(parts);
        }
    }
}
