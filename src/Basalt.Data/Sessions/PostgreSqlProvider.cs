using Npgsql;

namespace Basalt.Data.Sessions;

/// <summary>Opens PostgreSQL databases through an ordinary Npgsql connection string.</summary>
/// <remarks>
/// The connection is opened and so checked here, so a wrong password or an
/// unreachable server fails here with the server's own message.
/// </remarks>
public sealed class PostgreSqlProvider : IDatabaseProvider
{
    private const int DefaultPort = 5432;

    public DatabaseKind Kind => DatabaseKind.PostgreSql;

    /// <summary>What the explorer shows for a connection: the server, with its port when it is not the default, and the database. The password is never part of it.</summary>
    public static string DisplayNameOf(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var server = builder.Port == DefaultPort ? builder.Host : $"{builder.Host}:{builder.Port}";
        var database = builder.Database;

        return string.IsNullOrEmpty(database) ? server ?? "" : $"{server}/{database}";
    }

    public async Task<IDatabaseSession> OpenAsync(string connectionString, CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new PostgreSqlSession(connection, DisplayNameOf(connectionString));
    }
}
