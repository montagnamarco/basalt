using System.Text;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

[Collection(SharedStateCollection.Name)]
public class DataGridViewClipboardTests
{
    [Fact]
    public void Sparse_selection_preserves_blank_cells_and_escapes_csv_and_html()
    {
        using var grid = new System.Windows.Forms.DataGridView
        {
            AllowUserToAddRows = false,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
        };
        grid.Columns.Add("a", "A");
        grid.Columns.Add("b", "B");
        grid.Rows.Add("à,<one>", "unselected");
        grid.Rows.Add("unselected", "\"two\"");
        grid.Rows[0].Cells[0].Selected = true;
        grid.Rows[1].Cells[1].Selected = true;
        var data = grid.GetClipboardContent()!;
        Assert.Equal("à,<one>\t\r\n\t\"two\"", data.GetData(DataFormats.UnicodeText));
        Assert.Equal("\"à,<one>\",\r\n,\"\"\"two\"\"\"", data.GetData(DataFormats.CommaSeparatedValue));
        var html = Assert.IsType<string>(data.GetData(DataFormats.Html));
        var bytes = Encoding.UTF8.GetBytes(html);
        var lines = html.Split("\r\n");
        var start = int.Parse(lines.Single(line => line.StartsWith("StartFragment:")).Split(':')[1]);
        var end = int.Parse(lines.Single(line => line.StartsWith("EndFragment:")).Split(':')[1]);
        var fragment = Encoding.UTF8.GetString(bytes[start..end]);
        Assert.StartsWith("<table>", fragment);
        Assert.EndsWith("</table>", fragment);
        Assert.Contains("&lt;one&gt;", fragment);
    }

    [Fact]
    public void Disabled_copy_and_empty_selection_have_distinct_results()
    {
        using var grid = new System.Windows.Forms.DataGridView();
        Assert.Null(grid.GetClipboardContent());
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;
        Assert.Throws<InvalidOperationException>(() => grid.GetClipboardContent());
    }

    [Fact]
    public void Cell_clipboard_override_supplies_the_selected_text_format()
    {
        using var grid = new System.Windows.Forms.DataGridView
        {
            AllowUserToAddRows = false,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
        };
        grid.Columns.Add(new DataGridViewColumn(new ClipboardCell()));
        grid.Rows.Add("source");
        var cell = Assert.IsType<ClipboardCell>(grid.Rows[0].Cells[0]);
        cell.Selected = true;

        var data = grid.GetClipboardContent()!;

        Assert.Equal("custom text", data.GetData(DataFormats.Text));
        Assert.Equal("source", data.GetData(DataFormats.CommaSeparatedValue));
        Assert.Contains((DataFormats.Text, true, true, true, true), cell.Calls);
    }

    [Fact]
    public void Header_clipboard_overrides_supply_text_when_headers_are_included()
    {
        using var grid = new System.Windows.Forms.DataGridView
        {
            AllowUserToAddRows = false,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
        };
        grid.Columns.Add("a", "ordinary column");
        grid.Rows.Add("value");
        grid.Columns[0].HeaderCell = new ClipboardColumnHeaderCell();
        grid.Rows[0].HeaderCell = new ClipboardRowHeaderCell();
        grid.Rows[0].Cells[0].Selected = true;

        var data = grid.GetClipboardContent()!;

        Assert.Equal("\tcustom column\r\ncustom row\tvalue", data.GetData(DataFormats.Text));
    }

    [Fact]
    public void ControlC_copies_selected_cells_and_respects_a_handled_key_event()
    {
        using var grid = new KeyboardGrid { AllowUserToAddRows = false, ColumnCount = 1,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText };
        grid.Rows.Add("copied");
        grid.Rows[0].Cells[0].Selected = true;
        Clipboard.SetText("previous");
        try
        {
            var key = new KeyEventArgs(Keys.Control | Keys.C);
            grid.Key(key);
            Assert.True(key.Handled);
            Assert.Equal("copied", Clipboard.GetText());
            grid.KeyDown += (_, e) => e.Handled = true;
            Clipboard.SetText("handler wins");
            grid.Key(new KeyEventArgs(Keys.Control | Keys.C));
            Assert.Equal("handler wins", Clipboard.GetText());
        }
        finally { Clipboard.Clear(); }
    }

    private sealed class KeyboardGrid : System.Windows.Forms.DataGridView
    {
        internal void Key(KeyEventArgs e) => OnKeyDown(e);
    }

    private sealed class ClipboardCell : DataGridViewTextBoxCell
    {
        internal List<(string Format, bool FirstCell, bool LastCell, bool FirstRow, bool LastRow)> Calls { get; } = [];

        protected override object? GetClipboardContent(int rowIndex, bool firstCell, bool lastCell,
            bool inFirstRow, bool inLastRow, string format)
        {
            Calls.Add((format, firstCell, lastCell, inFirstRow, inLastRow));
            return format == DataFormats.Text ? "custom text" : base.GetClipboardContent(rowIndex,
                firstCell, lastCell, inFirstRow, inLastRow, format);
        }
    }

    private sealed class ClipboardColumnHeaderCell : DataGridViewColumnHeaderCell
    {
        protected override object? GetClipboardContent(int rowIndex, bool firstCell, bool lastCell,
            bool inFirstRow, bool inLastRow, string format) => format == DataFormats.Text
            ? "custom column\r\n"
            : base.GetClipboardContent(rowIndex, firstCell, lastCell, inFirstRow, inLastRow, format);
    }

    private sealed class ClipboardRowHeaderCell : DataGridViewRowHeaderCell
    {
        protected override object? GetClipboardContent(int rowIndex, bool firstCell, bool lastCell,
            bool inFirstRow, bool inLastRow, string format) => format == DataFormats.Text
            ? "custom row\t"
            : base.GetClipboardContent(rowIndex, firstCell, lastCell, inFirstRow, inLastRow, format);
    }
}
