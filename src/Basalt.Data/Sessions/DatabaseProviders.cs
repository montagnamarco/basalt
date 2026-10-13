using Basalt.Core.Localization;

namespace Basalt.Data.Sessions;

/// <summary>
/// The providers the IDE can open, one per engine.
/// </summary>
/// <remarks>
/// Each engine has one provider here, one line in <see cref="Registered"/>.
/// <see cref="Get"/> refuses an engine that has none, with a message the user can read.
/// </remarks>
public static class DatabaseProviders
{
    private static readonly IReadOnlyDictionary<DatabaseKind, IDatabaseProvider> Registered =
        new Dictionary<DatabaseKind, IDatabaseProvider>
        {
            [DatabaseKind.Sqlite] = new SqliteProvider(),

            [DatabaseKind.SqlServer] = new SqlServerProvider(),
            [DatabaseKind.PostgreSql] = new PostgreSqlProvider(),
            [DatabaseKind.MySql] = new MySqlProvider(),
        };

    /// <summary>The provider for an engine.</summary>
    /// <exception cref="NotSupportedException">The engine has no provider yet.</exception>
    public static IDatabaseProvider Get(DatabaseKind kind) =>
        Registered.TryGetValue(kind, out var provider)
            ? provider
            : throw new NotSupportedException(Localizer.Get(StringKeys.DatabaseUnsupported, kind.ToString()));
}
