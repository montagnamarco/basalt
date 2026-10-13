using Basalt.Core.Localization;
using Microsoft.Data.Sqlite;

namespace Basalt.Data.Sessions;

/// <summary>Opens SQLite database files.</summary>
/// <remarks>
/// The connection string is an ordinary SQLite one: <c>Data Source=path</c>, with
/// any other option the engine takes. SQLite is a file rather than a server, so
/// it works the moment the IDE is installed, with nothing to set up.
/// </remarks>
public sealed class SqliteProvider : IDatabaseProvider
{
    public DatabaseKind Kind => DatabaseKind.Sqlite;

    public async Task<IDatabaseSession> OpenAsync(string connectionString, CancellationToken ct = default)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var path = builder.DataSource;

        // Checked before opening: the default mode creates a missing file, and a
        // mistyped path would leave an empty database where the user expected their data.
        if (!File.Exists(path))
            throw new FileNotFoundException(Localizer.Get(StringKeys.DatabaseNoFile, path), path);

        if (builder.Mode == SqliteOpenMode.ReadWriteCreate)
            builder.Mode = SqliteOpenMode.ReadWrite;

        var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        try
        {
            // Opening succeeds on anything, a text file included: SQLite reads the
            // header only when asked. Asking now means a file picked by mistake
            // fails here rather than on the user's first query.
            await using var probe = connection.CreateCommand();
            probe.CommandText = "SELECT count(*) FROM sqlite_master";
            await probe.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (!ct.IsCancellationRequested)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidDataException(Localizer.Get(StringKeys.DatabaseNoFile, path), ex);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new SqliteSession(connection, Path.GetFileName(path));
    }
}
