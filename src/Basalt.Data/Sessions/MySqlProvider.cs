using MySqlConnector;

namespace Basalt.Data.Sessions;

/// <summary>Opens MySQL and MariaDB databases through an ordinary MySqlConnector connection string.</summary>
/// <remarks>
/// The connection is opened and so checked here, so a wrong password or an
/// unreachable server fails here with the server's own message.
/// </remarks>
public sealed class MySqlProvider : IDatabaseProvider
{
    private const uint DefaultPort = 3306;

    public DatabaseKind Kind => DatabaseKind.MySql;

    /// <summary>What the explorer shows for a connection: the server, with its port when it is not the default, and the database. The password is never part of it.</summary>
    public static string DisplayNameOf(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        var server = builder.Port == DefaultPort ? builder.Server : $"{builder.Server}:{builder.Port}";
        var database = builder.Database;

        return string.IsNullOrEmpty(database) ? server ?? "" : $"{server}/{database}";
    }

    public async Task<IDatabaseSession> OpenAsync(string connectionString, CancellationToken ct = default)
    {
        // UseAffectedRows is off, so an UPDATE that writes the value a row already has counts that row as
        // matched. Otherwise an edit that changes nothing would look like a row that vanished.
        // AllowUserVariables lets the session's own SQL use @variables, and is set here so the session never depends on the user's string.
        var builder = new MySqlConnectionStringBuilder(connectionString) { UseAffectedRows = false, AllowUserVariables = true };
        var connection = new MySqlConnection(builder.ConnectionString);

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new MySqlSession(connection, DisplayNameOf(connectionString));
    }
}
