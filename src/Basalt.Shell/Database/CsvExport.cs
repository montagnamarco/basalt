namespace Basalt.Shell.Database;

/// <summary>
/// Writes a CSV export on the thread pool, to a temporary file beside the target, and moves it over the target
/// only once every row has been written.
/// </summary>
/// <remarks>
/// A failure or a cancel part way through leaves the file the user already had, whole, and no temporary file
/// behind. The temporary file is in the target's folder, so the move is a rename rather than a copy across volumes.
/// </remarks>
public static class CsvExport
{
    /// <summary>
    /// Writes the headers, then the rows that <paramref name="writeRows"/> writes, and returns what it returns: the
    /// rows written. The delegate runs on the thread pool, so it reads its pages and formats its rows there.
    /// </summary>
    public static Task<int> WriteAsync(
        string path,
        IReadOnlyList<string> headers,
        Func<CsvWriter, CancellationToken, Task<int>> writeRows,
        CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var temporary = TemporaryBeside(path);

            try
            {
                int written;

                await using (var csv = new CsvWriter(temporary))
                {
                    await csv.WriteRowAsync(headers, ct).ConfigureAwait(false);
                    written = await writeRows(csv, ct).ConfigureAwait(false);
                }

                File.Move(temporary, path, overwrite: true);

                return written;
            }
            finally
            {
                TryDelete(temporary);
            }
        }, ct);

    private static string TemporaryBeside(string path)
    {
        var full = Path.GetFullPath(path);

        return Path.Combine(Path.GetDirectoryName(full)!, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
    }

    /// <summary>Deletes what is left of a failed export. A file that cannot be deleted must not hide the failure that left it.</summary>
    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do: the export has already failed or been cancelled, and that is what the caller hears about.
        }
    }
}
