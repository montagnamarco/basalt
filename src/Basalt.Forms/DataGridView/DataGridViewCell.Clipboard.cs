using System.Globalization;
using System.Net;

namespace System.Windows.Forms;

public abstract partial class DataGridViewCell
{
    /// <summary>Formats one cell with its row and column separators for the requested Clipboard format.</summary>
    protected virtual object? GetClipboardContent(int rowIndex, bool firstCell, bool lastCell,
        bool inFirstRow, bool inLastRow, string format)
    {
        if (DataGridView is null) throw new InvalidOperationException("The cell is not attached to a DataGridView.");
        if (ColumnIndex < 0) throw new InvalidOperationException("A row header must supply its own clipboard content.");
        if (rowIndex < 0 || rowIndex >= DataGridView.Rows.Count) throw new ArgumentOutOfRangeException(nameof(rowIndex));
        ArgumentNullException.ThrowIfNull(format);

        var value = Convert.ToString(GetEditedFormattedValue(rowIndex, DataGridViewDataErrorContexts.ClipboardContent),
            CultureInfo.CurrentCulture) ?? string.Empty;
        return FormatClipboardText(value, firstCell, lastCell, inLastRow, format);
    }

    internal object? GetClipboardContentCore(int rowIndex, bool firstCell, bool lastCell,
        bool inFirstRow, bool inLastRow, string format) =>
        GetClipboardContent(rowIndex, firstCell, lastCell, inFirstRow, inLastRow, format);

    internal static string? FormatClipboardText(string value, bool firstCell, bool lastCell,
        bool inLastRow, string format)
    {
        if (format == DataFormats.Text || format == DataFormats.UnicodeText)
            return value + (lastCell ? (inLastRow ? string.Empty : "\r\n") : "\t");

        if (format == DataFormats.CommaSeparatedValue)
        {
            var escaped = value.IndexOfAny([',', '"', '\r', '\n']) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
            return escaped + (lastCell ? (inLastRow ? string.Empty : "\r\n") : ",");
        }

        if (format == DataFormats.Html)
            return (firstCell ? "<tr>" : string.Empty) + "<td>" + WebUtility.HtmlEncode(value) + "</td>"
                + (lastCell ? "</tr>" : string.Empty);

        return null;
    }
}
