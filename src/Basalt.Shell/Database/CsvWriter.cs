using System.Text;

namespace Basalt.Shell.Database;

/// <summary>
/// Writes delimited text a row at a time, as rows are read, so a table of any size is exported in pages
/// rather than held in memory. The file is UTF-8 with a byte order mark, which spreadsheets read as UTF-8.
/// </summary>
public sealed class CsvWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly char _separator;

    public CsvWriter(string path, char separator = ',')
    {
        _writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _separator = separator;
    }

    /// <summary>Writes one row, asynchronously: the interface thread is not held while the disk takes it.</summary>
    public Task WriteRowAsync(IEnumerable<string?> fields, CancellationToken ct = default) =>
        _writer.WriteAsync(Line(fields, _separator).AsMemory(), ct);

    public async Task WriteRowsAsync(IEnumerable<IReadOnlyList<string?>> rows, CancellationToken ct = default)
    {
        foreach (var row in rows) await WriteRowAsync(row, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await _writer.DisposeAsync().ConfigureAwait(false);

    /// <summary>
    /// One row of text, ending in a line break. A field holding the separator, a quote or a line break is
    /// quoted and its quotes doubled, as RFC 4180 has it; NULL is an empty field.
    /// </summary>
    public static string Line(IEnumerable<string?> fields, char separator)
    {
        var text = new StringBuilder();
        var first = true;

        foreach (var field in fields)
        {
            if (!first) text.Append(separator);
            first = false;

            var value = field ?? "";

            if (value.IndexOfAny([separator, '"', '\r', '\n']) >= 0)
                text.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
            else
                text.Append(value);
        }

        return text.Append("\r\n").ToString();
    }
}
