using System.Data.Common;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Data.Sessions;
using Avalonia.Threading;
using Basalt.Workspace.Ai;

namespace Basalt.Shell.Database;

/// <summary>
/// Where a connection's password is kept: never in the settings file.
/// </summary>
/// <remarks>
/// The window and the explorer depend on this rather than on the keychain, so a
/// test can hand them an in-memory store and nothing reaches the OS. The real
/// one is <see cref="KeychainDatabasePasswordStore"/>.
/// </remarks>
public interface IDatabasePasswordStore
{
    string? Get(string connectionId);

    bool Set(string connectionId, string password);

    bool Remove(string connectionId);
}

/// <summary>Database passwords in the platform keychain, through the store the AI keys use.</summary>
public sealed class KeychainDatabasePasswordStore(ApiKeyStore keys) : IDatabasePasswordStore
{
    /// <summary>The keychain account of a connection's password: "database." and the connection's id.</summary>
    public static string AccountOf(string connectionId) => "database." + connectionId;

    public string? Get(string connectionId) => keys.GetSecret(AccountOf(connectionId));

    public bool Set(string connectionId, string password) => keys.SetSecret(AccountOf(connectionId), password);

    public bool Remove(string connectionId) => keys.RemoveSecret(AccountOf(connectionId));
}

/// <summary>An in-memory password store: nothing it holds reaches the keychain or a file.</summary>
public sealed class MemoryDatabasePasswordStore : IDatabasePasswordStore
{
    private readonly Dictionary<string, string> _passwords = new(StringComparer.Ordinal);

    public string? Get(string connectionId) => _passwords.GetValueOrDefault(connectionId);

    public bool Set(string connectionId, string password)
    {
        _passwords[connectionId] = password;
        return true;
    }

    public bool Remove(string connectionId) => _passwords.Remove(connectionId);
}

/// <summary>What the connection dialog collected. Field values as typed; <see cref="Password"/> is empty for none.</summary>
public sealed record DatabaseDraft(
    string Name,
    DatabaseKind Kind,
    string FilePath,
    string Server,
    string Port,
    string Database,
    string User,
    string Password,
    bool Integrated,
    bool Encrypt,
    bool TrustServerCertificate,
    string SslMode,
    string Advanced,
    bool ClearPassword = false)
{
    /// <summary>A draft with nothing filled in, for a new connection of one kind.</summary>
    public static DatabaseDraft Empty(DatabaseKind kind) => new(
        "", kind, "", "", "", "", "", "", false, false, false, "Prefer", "");
}

/// <summary>The engines the dialog and the explorer offer, and how each is named.</summary>
public static class DatabaseKinds
{
    /// <summary>Every engine the registry knows, in the order a user expects.</summary>
    public static IReadOnlyList<DatabaseKind> All { get; } = Enum.GetValues<DatabaseKind>();

    public static string Label(DatabaseKind kind) => Localizer.Get(kind switch
    {
        DatabaseKind.SqlServer => StringKeys.DbKindSqlServer,
        DatabaseKind.PostgreSql => StringKeys.DbKindPostgreSql,
        DatabaseKind.MySql => StringKeys.DbKindMySql,
        _ => StringKeys.DbKindSqlite,
    });

    /// <summary>The engine a settings file names; SQLite for anything unrecognised.</summary>
    public static DatabaseKind Parse(string name) =>
        Enum.TryParse<DatabaseKind>(name, out var kind) ? kind : DatabaseKind.Sqlite;
}

/// <summary>
/// Turns the dialog's fields into an ADO.NET connection string, and back.
/// </summary>
/// <remarks>
/// The strings use the keywords the engines' ADO.NET providers document
/// (Server, Database, User Id, Encrypt, Host, Username, SSL Mode...), so a
/// provider added later reads them without a change here. A string typed into
/// the advanced box is used as it is.
///
/// The password is the one part that is split off: <see cref="SplitPassword"/>
/// takes it out of a complete string, and <see cref="JoinPassword"/> puts it
/// back just before a session is opened. Only the split-off string is saved.
/// </remarks>
public static class ConnectionStrings
{
    private static readonly string[] PasswordKeys = ["Password", "Pwd"];

    /// <summary>The complete connection string for a draft, password included.</summary>
    public static string Build(DatabaseDraft draft)
    {
        // The advanced text stands for the fields, but a password typed in its field still goes in.
        if (draft.Advanced.Trim() is { Length: > 0 } advanced)
            return JoinPassword(advanced, draft.Password.Length > 0 ? draft.Password : null);

        var builder = new DbConnectionStringBuilder();
        var server = draft.Server.Trim();
        var port = draft.Port.Trim();
        var database = draft.Database.Trim();
        var user = draft.User.Trim();

        switch (draft.Kind)
        {
            case DatabaseKind.SqlServer:
                builder["Server"] = port.Length > 0 ? $"{server},{port}" : server;
                if (database.Length > 0) builder["Database"] = database;

                if (draft.Integrated)
                {
                    builder["Integrated Security"] = "True";
                }
                else
                {
                    builder["User Id"] = user;
                    if (draft.Password.Length > 0) builder["Password"] = draft.Password;
                }

                builder["Encrypt"] = draft.Encrypt ? "True" : "False";
                builder["TrustServerCertificate"] = draft.TrustServerCertificate ? "True" : "False";
                break;

            case DatabaseKind.PostgreSql:
                builder["Host"] = server;
                if (port.Length > 0) builder["Port"] = port;
                if (database.Length > 0) builder["Database"] = database;
                builder["Username"] = user;
                if (draft.Password.Length > 0) builder["Password"] = draft.Password;
                builder["SSL Mode"] = draft.SslMode;
                break;

            case DatabaseKind.MySql:
                builder["Server"] = server;
                if (port.Length > 0) builder["Port"] = port;
                if (database.Length > 0) builder["Database"] = database;
                builder["Uid"] = user;
                if (draft.Password.Length > 0) builder["Password"] = draft.Password;
                builder["SslMode"] = draft.SslMode;
                break;

            default:
                builder["Data Source"] = draft.FilePath.Trim();
                break;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// The draft a saved connection would be edited from: its fields read back out
    /// of the string, and the whole string as advanced text when the fields cannot
    /// rebuild it exactly.
    /// </summary>
    public static DatabaseDraft Parse(DatabaseConnectionSettings saved, string? password)
    {
        var kind = DatabaseKinds.Parse(saved.Kind);
        var builder = new DbConnectionStringBuilder();

        try
        {
            builder.ConnectionString = saved.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Not a string the builder reads: it stays whole, as advanced text.
            return DatabaseDraft.Empty(kind) with { Name = saved.Name, Advanced = saved.ConnectionString, Password = password ?? "" };
        }

        string Value(params string[] keys)
        {
            foreach (var key in keys)
                if (builder.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } text) return text;

            return "";
        }

        var serverValue = Value("Server", "Host", "Data Source");
        var comma = serverValue.LastIndexOf(',');
        var server = comma > 0 && kind == DatabaseKind.SqlServer ? serverValue[..comma] : serverValue;
        var port = comma > 0 && kind == DatabaseKind.SqlServer ? serverValue[(comma + 1)..] : Value("Port");

        var draft = new DatabaseDraft(
            saved.Name,
            kind,
            kind == DatabaseKind.Sqlite ? Value("Data Source") : "",
            kind == DatabaseKind.Sqlite ? "" : server,
            port,
            Value("Database", "Initial Catalog"),
            Value("User Id", "User ID", "Username", "Uid", "User"),
            password ?? "",
            Value("Integrated Security", "Trusted_Connection") is "True" or "true" or "SSPI",
            Value("Encrypt").Equals("True", StringComparison.OrdinalIgnoreCase),
            Value("TrustServerCertificate").Equals("True", StringComparison.OrdinalIgnoreCase),
            Value("SSL Mode", "SslMode") is { Length: > 0 } ssl ? ssl : "Prefer",
            "");

        // Rebuilt from the fields and compared with what is stored: when they differ, the
        // stored text has something the fields do not show, and it is kept as advanced text.
        var rebuilt = Build(draft with { Password = "" });

        return rebuilt == saved.ConnectionString ? draft : draft with { Advanced = saved.ConnectionString };
    }

    /// <summary>Takes the password out of a complete connection string.</summary>
    public static (string Plain, string? Password) SplitPassword(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        string? password = null;

        foreach (var key in PasswordKeys)
        {
            if (!builder.TryGetValue(key, out var value)) continue;

            password ??= value?.ToString();
            builder.Remove(key);
        }

        return (builder.ConnectionString, string.IsNullOrEmpty(password) ? null : password);
    }

    /// <summary>Puts a password into a connection string that has none.</summary>
    public static string JoinPassword(string connectionString, string? password)
    {
        if (string.IsNullOrEmpty(password)) return connectionString;

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        builder["Password"] = password;

        return builder.ConnectionString;
    }

    /// <summary>
    /// The password a complete connection string carries, wherever it was typed: in the password field or in the
    /// advanced text. Null when it has none, or when the builder does not read the string.
    /// </summary>
    public static string? PasswordIn(string connectionString)
    {
        try
        {
            return SplitPassword(connectionString).Password;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Whether a draft names a connection that needs no password at all.</summary>
    public static bool NeedsNoPassword(DatabaseDraft draft) =>
        draft.Kind == DatabaseKind.Sqlite || draft.Integrated;
}

/// <summary>
/// The saved connections and the sessions open on them.
/// </summary>
/// <remarks>
/// Everything that can block (the settings file, the keychain, a provider's connect)
/// runs on the thread pool. One cache lock guards the connection list, the open
/// sessions and the per-connection gates; it is never held across an await or a
/// provider call. Each connection has its own gate, so a slow connect to one does
/// not stall another. A change is built beside the cache and written to the settings
/// file first; only then does it become the cache. When the write fails, the cache and
/// the keychain are left as they were. Changes are made one at a time, so two edits
/// cannot overwrite each other, and a change holds its connection's gate, so a connect
/// cannot read the settings being replaced.
/// </remarks>
public sealed class DatabaseWorkspace : IAsyncDisposable
{
    private readonly SettingsStore _settings;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, IDatabaseSession> _sessions = new(StringComparer.Ordinal);

    // How many times each connection's settings were saved by an edit. A tab records the number it read
    // under, and refuses to save under another: the gate orders an edit and a save, but a save that passed
    // its stale check before the edit began would otherwise write to the database the edit points at.
    private readonly Dictionary<string, int> _settingsVersions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly Task _loaded;
    private List<DatabaseConnectionSettings> _connections = [];
    private Func<DatabaseKind, IDatabaseProvider>? _providerOverride;
    private Func<IdeSettings, bool>? _saveOverride;
    private bool _disposed;

    /// <summary>Held while a change is written: a change reads the list it changes, so two must not interleave.</summary>
    private readonly object _writeLock = new();

    public DatabaseWorkspace(SettingsStore settings, IDatabasePasswordStore passwords)
    {
        _settings = settings;
        Passwords = passwords;
        History = new QueryHistory(QueryHistory.PathBeside(settings.FilePath));

        // The settings file is read on the pool: the window is being built on the interface thread.
        _loaded = Task.Run(() =>
        {
            var list = settings.Load().Database.Connections;

            lock (_cacheLock) _connections = list;
        }).ContinueWith(_ => Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty)), TaskScheduler.Default);
    }

    /// <summary>The last queries run on each connection, kept beside the settings file.</summary>
    public QueryHistory History { get; }

    /// <summary>Where passwords are kept. Replaced by a test before any connection is saved.</summary>
    public IDatabasePasswordStore Passwords { get; private set; }

    /// <summary>Raised on the interface thread when the saved list is read, added to, changed or removed.</summary>
    public event EventHandler? Changed;

    /// <summary>The saved connections, in the order they were added. Empty until the settings file has been read.</summary>
    public IReadOnlyList<DatabaseConnectionSettings> Connections
    {
        get
        {
            lock (_cacheLock) return _connections.Select(Copy).ToList();
        }
    }

    public DatabaseConnectionSettings? Find(string connectionId)
    {
        lock (_cacheLock) return _connections.Find(c => c.Id == connectionId) is { } found ? Copy(found) : null;
    }

    /// <summary>Whether a session is open on a connection right now.</summary>
    public bool IsOpen(string connectionId)
    {
        lock (_cacheLock) return _sessions.ContainsKey(connectionId);
    }

    /// <summary>Whether any session is open, so that closing the window has something to release.</summary>
    internal bool HasOpenSessions
    {
        get
        {
            lock (_cacheLock) return _sessions.Count > 0;
        }
    }

    /// <summary>
    /// Puts a session in place of the one a connection would open. For the tests only: a test scripts
    /// an answer the engine does not give yet, or wraps a real session to make it fail on purpose.
    /// </summary>
    internal void AdoptSessionForTests(string connectionId, IDatabaseSession session)
    {
        lock (_cacheLock) _sessions[connectionId] = session;
    }

    /// <summary>Replaces the password store. For tests, which must not reach the keychain.</summary>
    public void UsePasswords(IDatabasePasswordStore passwords) => Passwords = passwords;

    /// <summary>Replaces the providers, by engine. For tests, which need a provider that hangs or fails on purpose.</summary>
    internal void UseProvidersForTests(Func<DatabaseKind, IDatabaseProvider> providerFor) => _providerOverride = providerFor;

    /// <summary>Replaces the settings write. For tests, which make it fail to check what a failed change leaves behind.</summary>
    internal void UseSettingsSaveForTests(Func<IdeSettings, bool>? save) => _saveOverride = save;

    /// <summary>Saves a new connection; its password, if any, goes to the password store.</summary>
    public async Task<DatabaseConnectionSettings> AddAsync(DatabaseDraft draft)
    {
        await _loaded.ConfigureAwait(false);

        return await Task.Run(() =>
        {
            var saved = new DatabaseConnectionSettings { Name = NameOf(draft), Kind = draft.Kind.ToString() };
            var plan = PlanSecrets(draft, keepStoredPassword: false);

            saved.ConnectionString = plan.Plain;

            lock (_writeLock)
            {
                var next = Snapshot();
                next.Add(saved);

                Commit(saved.Id, plan, next);
            }

            return saved;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Changes a saved connection. An open session is closed first, so the next use connects
    /// with the new settings. An empty password keeps the stored one, unless the draft asks to clear it.
    /// </summary>
    /// <remarks>
    /// The connection's gate is held from the close to the write, so a connect that starts meanwhile waits for
    /// the new settings rather than reading the old ones. The cache takes the new entry only after the settings
    /// are written.
    /// </remarks>
    public async Task UpdateAsync(string connectionId, DatabaseDraft draft, CancellationToken ct = default)
    {
        await _loaded.ConfigureAwait(false);

        var gate = GateFor(connectionId);

        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await CloseLockedAsync(connectionId).ConfigureAwait(false);

            await Task.Run(() =>
            {
                var plan = PlanSecrets(draft, keepStoredPassword: true);

                lock (_writeLock)
                {
                    var next = Snapshot();
                    var index = next.FindIndex(c => c.Id == connectionId);

                    if (index < 0) throw new InvalidOperationException(Localizer.Get(StringKeys.DbOpenFailed, connectionId, ""));

                    next[index] = new DatabaseConnectionSettings
                    {
                        Id = connectionId,
                        Name = NameOf(draft),
                        Kind = draft.Kind.ToString(),
                        ConnectionString = plan.Plain,
                    };

                    Commit(connectionId, plan, next);
                }

                lock (_cacheLock)
                    _settingsVersions[connectionId] = SettingsVersionLocked(connectionId) + 1;
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The number of edits saved to a connection's settings so far; see <see cref="_settingsVersions"/>.</summary>
    public int SettingsVersionOf(string connectionId)
    {
        lock (_cacheLock) return SettingsVersionLocked(connectionId);
    }

    private int SettingsVersionLocked(string connectionId) =>
        _settingsVersions.TryGetValue(connectionId, out var version) ? version : 0;

    /// <summary>
    /// Forgets a connection, its open session and its password. The settings are written first: when that
    /// fails, the connection stays, with its password.
    /// </summary>
    public async Task RemoveAsync(string connectionId, CancellationToken ct = default)
    {
        await _loaded.ConfigureAwait(false);

        var gate = GateFor(connectionId);

        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await CloseLockedAsync(connectionId).ConfigureAwait(false);

            await Task.Run(() =>
            {
                lock (_writeLock)
                {
                    var next = Snapshot();
                    next.RemoveAll(c => c.Id == connectionId);

                    // The stored password goes with the connection; when the settings cannot be written it comes back.
                    Commit(connectionId, new SecretPlan(string.Empty, SecretAction.Remove, null), next);
                }
            }, ct).ConfigureAwait(false);

            // Its queries go with it: a connection added later never reuses the id, and the file should not grow for ever.
            await History.ForgetAsync(connectionId).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The open session on a connection, opening it when none is open. The connect happens on the connection's
    /// own gate, so other connections are not held up by it.
    /// </summary>
    /// <exception cref="NotSupportedException">The engine has no provider yet.</exception>
    public Task<IDatabaseSession> OpenAsync(string connectionId, CancellationToken ct = default) =>
        OpenUnderGateAsync(connectionId, reconnect: false, ct);

    /// <summary>
    /// Closes the session on a connection, if one is open, and connects again with the connection's current settings.
    /// For Reconnect: the cache would otherwise hand back the very session that failed.
    /// </summary>
    /// <exception cref="NotSupportedException">The engine has no provider yet.</exception>
    public Task<IDatabaseSession> ReconnectAsync(string connectionId, CancellationToken ct = default) =>
        OpenUnderGateAsync(connectionId, reconnect: true, ct);

    private async Task<IDatabaseSession> OpenUnderGateAsync(string connectionId, bool reconnect, CancellationToken ct)
    {
        await _loaded.ConfigureAwait(false);

        var gate = GateFor(connectionId);

        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            lock (_cacheLock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(DatabaseWorkspace));
                if (!reconnect && _sessions.TryGetValue(connectionId, out var open)) return open;
            }

            if (reconnect)
            {
                // The session being replaced may be the broken one, and its close may fail too: that must not stop the new connect.
                try
                {
                    await CloseLockedAsync(connectionId).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Dropped from the cache before it was disposed, so the connect below does not see it.
                }
            }

            // The keychain may run a system tool, so the password is read on the pool.
            var (kind, full, password) = await Task.Run(() =>
            {
                var saved = Find(connectionId)
                    ?? throw new InvalidOperationException(Localizer.Get(StringKeys.DbOpenFailed, connectionId, ""));

                var stored = Passwords.Get(connectionId);

                return (DatabaseKinds.Parse(saved.Kind), ConnectionStrings.JoinPassword(saved.ConnectionString, stored), stored);
            }, ct).ConfigureAwait(false);

            var provider = ProviderFor(kind);

            IDatabaseSession session;

            try
            {
                session = await provider.OpenAsync(full, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not NotSupportedException)
            {
                // A provider's message can quote the connection string or the password, in either of the two fields
                // the password may be typed in; neither is shown.
                throw new InvalidOperationException(
                    SecretScrub.Clean(ex.Message, full, password, ConnectionStrings.PasswordIn(full)), ex);
            }

            bool closed;

            lock (_cacheLock)
            {
                // Checked with the cache, so a close that began while the connect was in flight is not missed.
                closed = _disposed;
                if (!closed) _sessions[connectionId] = session;
            }

            if (!closed) return session;

            // The workspace closed while this connect was running: nothing would dispose the session now, so it is closed here.
            await session.DisposeAsync().ConfigureAwait(false);

            throw new ObjectDisposedException(nameof(DatabaseWorkspace));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Closes the session on a connection, if one is open. The saved connection stays.</summary>
    public async Task CloseAsync(string connectionId, CancellationToken ct = default)
    {
        var gate = GateFor(connectionId);

        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await CloseLockedAsync(connectionId).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Takes the cached session out and disposes it. The caller holds the connection's gate.</summary>
    private async Task CloseLockedAsync(string connectionId)
    {
        IDatabaseSession? session;

        lock (_cacheLock) _sessions.Remove(connectionId, out session);

        if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a connection described by a draft and closes it again at once: the
    /// check behind Test Connection. Returns the engine's own description of what
    /// it connected to; a failure is thrown with the engine's message, without the password.
    /// </summary>
    public static Task<string> TestAsync(DatabaseDraft draft, CancellationToken ct = default) =>
        TestAsync(draft, DatabaseProviders.Get(draft.Kind), ct);

    /// <summary>The check behind Test Connection, against the given provider. For tests, which need one that fails on purpose.</summary>
    internal static async Task<string> TestAsync(DatabaseDraft draft, IDatabaseProvider provider, CancellationToken ct)
    {
        var full = ConnectionStrings.Build(draft);

        try
        {
            await using var session = await provider.OpenAsync(full, ct).ConfigureAwait(false);

            return session.DisplayName;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not NotSupportedException)
        {
            // The password may sit in the advanced text alone, where the password field is empty.
            throw new InvalidOperationException(
                SecretScrub.Clean(ex.Message, full, draft.Password, ConnectionStrings.PasswordIn(full)), ex);
        }
    }

    /// <summary>
    /// Closes every open session. Running statements are stopped by the sessions' own disposal. A session that
    /// fails to close does not keep the others open: every one is closed, and the failures are thrown together.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        lock (_cacheLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        List<string> ids;
        lock (_cacheLock) ids = _sessions.Keys.ToList();

        List<Exception> failures = [];

        foreach (var id in ids)
        {
            try
            {
                await CloseAsync(id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private IDatabaseProvider ProviderFor(DatabaseKind kind) => _providerOverride?.Invoke(kind) ?? DatabaseProviders.Get(kind);

    /// <summary>The gate of one connection, made on first use.</summary>
    private SemaphoreSlim GateFor(string connectionId)
    {
        lock (_cacheLock)
        {
            if (!_gates.TryGetValue(connectionId, out var gate)) _gates[connectionId] = gate = new SemaphoreSlim(1, 1);

            return gate;
        }
    }

    /// <summary>What a change does to the stored password, once its settings are known.</summary>
    private enum SecretAction
    {
        /// <summary>Leave the stored password as it is.</summary>
        Keep,

        /// <summary>Forget the stored password: the connection needs none, or the user cleared it.</summary>
        Remove,

        /// <summary>Store the password the draft carries.</summary>
        Set,
    }

    /// <summary>The connection string to save, without its password, and the secret change the draft asks for.</summary>
    private sealed record SecretPlan(string Plain, SecretAction Action, string? Password);

    /// <summary>Splits a draft into the string to save and the secret change it asks for. Touches neither the keychain nor the cache.</summary>
    private static SecretPlan PlanSecrets(DatabaseDraft draft, bool keepStoredPassword)
    {
        var (plain, password) = ConnectionStrings.SplitPassword(ConnectionStrings.Build(draft));

        if (draft.ClearPassword || ConnectionStrings.NeedsNoPassword(draft)) return new(plain, SecretAction.Remove, null);
        if (password is not null) return new(plain, SecretAction.Set, password);

        return new(plain, keepStoredPassword ? SecretAction.Keep : SecretAction.Remove, null);
    }

    /// <summary>
    /// Applies the secret change, then writes the settings holding <paramref name="next"/>. When the settings cannot
    /// be written, the secret goes back as it was: a failed edit keeps the stored password, and a failed add leaves
    /// no orphan password behind. The caller holds <see cref="_writeLock"/>.
    /// </summary>
    private void Commit(string connectionId, SecretPlan plan, List<DatabaseConnectionSettings> next)
    {
        if (plan.Action == SecretAction.Keep)
        {
            WriteSettings(next);
            return;
        }

        var previous = Passwords.Get(connectionId);

        ApplySecret(connectionId, plan);

        try
        {
            WriteSettings(next);
        }
        catch
        {
            if (previous is null) Passwords.Remove(connectionId);
            else Passwords.Set(connectionId, previous);

            throw;
        }
    }

    private void ApplySecret(string connectionId, SecretPlan plan)
    {
        if (plan.Action == SecretAction.Remove)
        {
            Passwords.Remove(connectionId);
        }
        else if (plan.Action == SecretAction.Set && !Passwords.Set(connectionId, plan.Password!))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.DbPasswordNotStored));
        }
    }

    /// <summary>
    /// Writes <paramref name="next"/> into the settings file, keeping every other setting as it is on disk, and makes
    /// it the cache. The cache changes only once the write has succeeded.
    /// </summary>
    private void WriteSettings(List<DatabaseConnectionSettings> next)
    {
        var settings = _settings.Load();

        settings.Database.Connections = next.Select(Copy).ToList();

        var saved = _saveOverride?.Invoke(settings) ?? _settings.Save(settings);

        if (!saved) throw new IOException(Localizer.Get(StringKeys.DbSettingsNotSaved));

        lock (_cacheLock) _connections = next;

        // Raised on the interface thread: its listeners rebuild controls.
        Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>A copy of the cached list, to change and then write.</summary>
    private List<DatabaseConnectionSettings> Snapshot()
    {
        lock (_cacheLock) return _connections.Select(Copy).ToList();
    }

    private static DatabaseConnectionSettings Copy(DatabaseConnectionSettings c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Kind = c.Kind,
        ConnectionString = c.ConnectionString,
    };

    private static string NameOf(DatabaseDraft draft)
    {
        if (draft.Name.Trim() is { Length: > 0 } name) return name;

        return draft.Kind == DatabaseKind.Sqlite
            ? Path.GetFileName(draft.FilePath.Trim())
            : draft.Server.Trim();
    }
}

/// <summary>Takes the secrets out of a message before it is shown or logged.</summary>
public static class SecretScrub
{
    /// <summary>The message with each secret replaced by a mask. Null and empty secrets are skipped.</summary>
    public static string Clean(string message, params string?[] secrets)
    {
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret)) message = message.Replace(secret, "••••", StringComparison.Ordinal);
        }

        return message;
    }
}
