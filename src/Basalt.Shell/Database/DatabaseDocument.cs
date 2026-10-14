using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Data.Sessions;
using Basalt.Shell.Controls;
using Dock.Model.Mvvm.Controls;

namespace Basalt.Shell.Database;

/// <summary>What a Table Data or Query document offers the window: whether it has changes to lose, and how to keep them.</summary>
public interface IDatabaseDocumentContent
{
    /// <summary>Whether closing the document would lose something the database does not have.</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>Writes the changes. Returns false, with the reason shown in the document, when they were not written.</summary>
    Task<bool> SaveAsync();
}

/// <summary>
/// A document tab that holds a database view rather than a file: the rows of a
/// table, or a query.
/// </summary>
public sealed class DatabaseDocument : Document
{
    public DatabaseDocument(string title, Control content)
    {
        Id = Guid.NewGuid().ToString("N");
        Title = title;
        View = content;
    }

    /// <summary>The view shown in the tab; always an <see cref="IDatabaseDocumentContent"/>.</summary>
    /// <remarks>
    /// Not named Content: Dock's templates bind a dockable's Content by name in more than one place (the
    /// document area and the tab's compact close button), and a control can have only one parent. The dock
    /// gets the view from the window's data template instead, which caches it per document.
    /// </remarks>
    public Control View { get; }

    public IDatabaseDocumentContent Editor => (IDatabaseDocumentContent)View;
}

/// <summary>
/// The pieces every database grid shares: the NULL style, the read-only result
/// grid, and the text a row becomes when it is copied or exported.
/// </summary>
internal static class DatabaseGrids
{
    /// <summary>
    /// A grid over result columns and rows, read-only, with NULL shown as grey italics.
    /// </summary>
    public static DataGrid ReadOnly(IReadOnlyList<ResultColumnInfo> columns, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            FontSize = PanelMetrics.FontSize,
            ItemsSource = rows,
        };

        for (var i = 0; i < columns.Count; i++)
        {
            var at = i;

            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = columns[i].Name,
                CellTemplate = new FuncDataTemplate<IReadOnlyList<object?>>(
                    (row, _) => CellView(row is not null && at < row.Count ? row[at] : null),
                    supportsRecycling: false),
            });
        }

        return grid;
    }

    /// <summary>One cell of a read-only grid: the value's text, or NULL in grey italics.</summary>
    public static Control CellView(object? value)
    {
        var text = new TextBlock
        {
            FontSize = PanelMetrics.FontSize,
            Margin = new Thickness(Spacing.Tight, 2),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        if (value is null)
        {
            text.Text = Localizer.Get(StringKeys.DbNull);
            text.FontStyle = FontStyle.Italic;
            text.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");
        }
        else
        {
            text.Text = CellText.Of(value);
        }

        return text;
    }

    /// <summary>
    /// Comma- or tab-separated text of rows, for the clipboard. The same escaping as the files: see
    /// <see cref="CsvWriter.Line"/>.
    /// </summary>
    public static string Delimited(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows,
        bool includeHeaders,
        char separator)
    {
        var text = new StringBuilder();

        if (includeHeaders) text.Append(CsvWriter.Line(headers, separator));

        foreach (var row in rows) text.Append(CsvWriter.Line(row, separator));

        return text.ToString();
    }
}
