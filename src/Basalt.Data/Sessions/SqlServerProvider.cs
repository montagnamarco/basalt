using Microsoft.Data.SqlClient;

namespace Basalt.Data.Sessions;

/// <summary>Opens SQL Server databases through an ordinary ADO.NET connection string.</summary>
/// <remarks>
/// Microsoft.Data.SqlClient 6 encrypts by default (Encrypt=true). A server whose
/// certificate is not trusted, such as a local or container server with a
/// self-signed one, needs TrustServerCertificate=true in the connection string.
/// The default stays secure; the setting is the user's to make.
///
/// The connection is opened and so checked here, so a wrong password or an
/// unreachable server fails here with SQL Server's own message, not on the first query.
/// </remarks>
public sealed class SqlServerProvider : IDatabaseProvider
{
    public DatabaseKind Kind => DatabaseKind.SqlServer;

    /// <summary>What the explorer shows for a connection: the server and the database. The password is never part of it.</summary>
    public static string DisplayNameOf(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var server = builder.DataSource;
        var database = builder.InitialCatalog;

        return string.IsNullOrEmpty(database) ? server : $"{server}/{database}";
    }

    public async Task<IDatabaseSession> OpenAsync(string connectionString, CancellationToken ct = default)
    {
        var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new SqlServerSession(connection, DisplayNameOf(connectionString));
    }
}
