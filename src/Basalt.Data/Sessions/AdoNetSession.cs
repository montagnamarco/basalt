using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Basalt.Core.Localization;

namespace Basalt.Data.Sessions;

/// <summary>
/// The engine-independent part of a session over a System.Data.Common connection.
/// </summary>
/// <remarks>
/// SQL Server, PostgreSQL and MySQL differ in their catalogues, their parameter
/// markers and their dialect, and share the rest: the one-operation gate, the
/// worker thread, a batch that keeps the results read before an error, paging,
/// and edits in one transaction. An engine supplies the hooks below and nothing
/// more; the public methods are the same for every engine.
///
/// Cancelling a call interrupts the statement through <see cref="Interrupt"/>
/// and returns only when the worker has finished with the connection. A close,
/// or the next call, must not reach a connection while a statement still runs
/// on it, and a statement that is only abandoned keeps running on the server.
/// </remarks>
public abstract class AdoNetSession : IDatabaseSession
{
    /// <summary>
    /// One operation on the connection at a time. A provider connection is not
    /// made for two threads at once, so a read of the schema waits for a query
    /// that is still running, and a close waits for both.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The statement the worker is running now, or null. Read by a close, which must stop it first.</summary>
    private DbCommand? _running;

    /// <summary>1 once a close has begun. A statement that starts after that is refused, so a close never waits for a query it has not yet seen.</summary>
    private int _disposing;

    protected AdoNetSession(DbConnection connection, string displayName)
    {
        Connection = connection;
        DisplayName = displayName;
    }

    /// <summary>The connection this session owns: disposing the session closes it.</summary>
    protected DbConnection Connection { get; }

    public string DisplayName { get; }

    public abstract DatabaseKind Kind { get; }

    public abstract ISqlDialect Dialect { get; }

    public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(CancellationToken ct = default) =>
        RunAsync(connection => ReadObjectsAsync(connection, ct), ct);

    public Task<TableDetails> GetDetailsAsync(DbObjectName name, CancellationToken ct = default) =>
        RunAsync(connection => ReadDetailsAsync(connection, name, ct), ct);

    public Task<QueryBatchResult> ExecuteAsync(string sql, int maxRows = 10_000, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRows);

        return RunAsync(connection => RunBatchAsync(connection, sql, maxRows, ct), ct);
    }

    public async Task<DataPage> ReadPageAsync(DataPageRequest request, CancellationToken ct = default)
    {
        // Checked before the worker starts, so a bad page is an argument error, not an engine one.
        SqlFilter.CheckPage(request.Offset, request.Limit);

        return await RunAsync(connection => ReadPageCoreAsync(connection, request, ct), ct).ConfigureAwait(false);
    }

    public Task<int> ApplyEditsAsync(DbObjectName table, IReadOnlyList<RowEdit> edits, CancellationToken ct = default) =>
        RunAsync(connection => ApplyEditsCoreAsync(connection, table, edits, ct), ct);

    public async ValueTask DisposeAsync()
    {
        // A statement still running would make the wait below last as long as the
        // statement, which the user may have stopped already. Stop it first; the
        // worker then finishes with the connection and releases the gate.
        // Interlocked writes are full fences: with the worker's write-then-read of _disposing, at least one
        // of the two sees the other, so a statement is either stopped here or refused in RunCommandAsync.
        Interlocked.Exchange(ref _disposing, 1);

        var running = Volatile.Read(ref _running);

        if (running is not null) TryInterrupt(running);

        // Waits for a statement still on the connection, as a close does in DatabaseClient.
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The tables and views the user can see, system objects left out, ordered by schema then name.</summary>
    protected abstract Task<IReadOnlyList<DbObjectInfo>> ReadObjectsAsync(DbConnection connection, CancellationToken ct);

    /// <summary>Columns, indexes and foreign keys of one table or view, read from the engine's catalogue.</summary>
    protected abstract Task<TableDetails> ReadDetailsAsync(DbConnection connection, DbObjectName name, CancellationToken ct);

    /// <summary>
    /// The name of the Nth parameter, marker included: "@p0". Engines whose
    /// markers differ override this; the name is also written into the SQL.
    /// </summary>
    protected virtual string ParameterName(int index) => $"@p{index}";

    /// <summary>
    /// The clause that inserts a row with no values given. MySQL writes
    /// <c>() VALUES ()</c> instead, and overrides this.
    /// </summary>
    protected virtual string EmptyInsertClause => "DEFAULT VALUES";

    /// <summary>
    /// Makes a parameter for a command. Engines that need a provider-specific
    /// parameter type, such as PostgreSQL, which refuses a text parameter for an
    /// integer column, override this and use <paramref name="column"/> to choose it.
    /// </summary>
    protected virtual DbParameter CreateParameter(DbCommand command, string name, object? value, ColumnInfo column)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }

    /// <summary>
    /// The value a typed text becomes for a column. Text stays text; null stays
    /// null. A text that does not fit the column's type is a <see cref="FormatException"/>,
    /// which the edit it belongs to reports. Numbers are read in the invariant
    /// culture, whatever the user's culture, so "1.5" is the same number everywhere.
    /// </summary>
    protected virtual object? ConvertValue(string? text, ColumnInfo column)
    {
        if (text is null) return null;

        var kind = KindOf(column.DataType);
        var trimmed = text.Trim();

        return kind switch
        {
            ValueKind.Integer => long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
                ? integer : throw BadValue(text, kind, column),
            ValueKind.Decimal => decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
                ? exact : throw BadValue(text, kind, column),

            // NaN is refused rather than stored: SQLite keeps it as NULL, which would change the value without a word.
            ValueKind.Real => double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) && !double.IsNaN(real)
                ? real : throw BadValue(text, kind, column),
            ValueKind.Boolean => trimmed.ToLowerInvariant() switch
            {
                "true" or "1" => true,
                "false" or "0" => false,
                _ => throw BadValue(text, kind, column),
            },
            ValueKind.Date => DateTime.TryParseExact(trimmed, IsoDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date : throw BadValue(text, kind, column),
            ValueKind.Guid => Guid.TryParse(trimmed, out var guid) ? guid : throw BadValue(text, kind, column),
            ValueKind.Binary => ParseHex(trimmed) ?? throw BadValue(text, kind, column),
            _ => text,
        };
    }

    /// <summary>
    /// The batch line an engine error is about, when the exception says which
    /// line it was. Most engines do not, so the default is none.
    /// </summary>
    protected virtual int? ErrorLine(DbException exception) => null;

    /// <summary>
    /// The batch line an engine error is about, given the batch that was run.
    /// An engine that reports a character position rather than a line (PostgreSQL)
    /// converts it here, which needs the text. The default defers to <see cref="ErrorLine(DbException)"/>.
    /// </summary>
    protected virtual int? ErrorLine(DbException exception, string batch, int statement) => ErrorLine(exception, batch);

    /// <summary>
    /// The batch line an engine error is about, given the batch and the statement that failed, counted
    /// from zero: the batch's statements answer one after another. Engines whose error positions are
    /// relative to the statement (PostgreSQL) need both. The default defers to the overload above.
    /// </summary>
    protected virtual int? ErrorLine(DbException exception, string batch) => ErrorLine(exception);

    /// <summary>
    /// Listens for the engine's own messages while a batch runs: SQL Server's
    /// PRINT, PostgreSQL's RAISE NOTICE. The listener is removed when the returned
    /// object is disposed. Most engines send none, so the default listens to nothing.
    /// </summary>
    protected virtual IDisposable? ListenForMessages(DbCommand command, Action<QueryMessage> report) => null;

    /// <summary>
    /// Whether a batch reports "N rows affected" from the reader's RecordsAffected. SQL Server fills
    /// that only when the reader closes, so it reports its counts through its own listener instead.
    /// </summary>
    protected virtual bool ReportsRowCounts => true;

    /// <summary>
    /// Stops the statement running on this command. The default is
    /// <c>DbCommand.Cancel</c>; an engine whose provider ignores it calls the
    /// engine's own interrupt instead. Must not throw: it runs in the
    /// cancellation callback and in a close.
    /// </summary>
    protected virtual void Interrupt(DbCommand command) => command.Cancel();

    /// <summary>
    /// Calls <see cref="Interrupt"/> and swallows what it throws: an interrupt runs in a cancellation
    /// callback and in a close, and a close must still dispose the connection.
    /// </summary>
    private void TryInterrupt(DbCommand command)
    {
        try
        {
            Interrupt(command);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Runs one statement with its cancellation wired to <see cref="Interrupt"/>.
    /// A statement that fails because it was cancelled is reported as the cancellation.
    /// </summary>
    protected async Task<T> RunCommandAsync<T>(DbCommand command, CancellationToken ct, Func<Task<T>> body)
    {
        ct.ThrowIfCancellationRequested();

        // Registered for the length of the statement only: a Stop after it has
        // finished finds nothing running, and the engine has nothing to interrupt.
        using var interrupt = ct.Register(() => TryInterrupt(command));

        // Published before the check below, so a close that looks for the statement either finds it or was seen here.
        Interlocked.Exchange(ref _running, command);

        try
        {
            if (Volatile.Read(ref _disposing) == 1)
                throw new OperationCanceledException(ct);

            return await body().ConfigureAwait(false);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // An interrupted statement reports an engine error. The caller asked for the stop.
            throw new OperationCanceledException(ct);
        }
        finally
        {
            Interlocked.Exchange(ref _running, null);
        }
    }

    /// <summary>One query, read whole, capped at <paramref name="maxRows"/>.</summary>
    protected async Task<ResultSet> ReadOneSetAsync(DbConnection connection, string sql, int maxRows, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await RunCommandAsync<ResultSet>(command, ct, async () =>
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await ReadSetAsync(reader, maxRows, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>The first cell of the first row of a query, as the engine returns it.</summary>
    protected async Task<object?> QueryScalarAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await RunCommandAsync<object?>(command, ct, () => command.ExecuteScalarAsync(ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one piece of work on the connection, on a worker thread, after any
    /// earlier piece has finished with the connection.
    /// </summary>
    /// <remarks>
    /// The gate is released only when the work has really finished, including
    /// when it was cancelled: the statement stops through <see cref="Interrupt"/>,
    /// the worker returns, and only then may another call use the connection.
    /// </remarks>
    private async Task<T> RunAsync<T>(Func<DbConnection, Task<T>> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await Task.Run(() => work(Connection)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads one result set's columns and rows. A row past <paramref name="maxRows"/>
    /// is read only to learn that there is one, so a set of exactly that size is not marked truncated.
    /// </summary>
    private static async Task<ResultSet> ReadSetAsync(DbDataReader reader, int maxRows, CancellationToken ct)
    {
        var columns = new ResultColumnInfo[reader.FieldCount];

        for (var i = 0; i < columns.Length; i++)
            columns[i] = new ResultColumnInfo(reader.GetName(i), reader.GetDataTypeName(i));

        var rows = new List<IReadOnlyList<object?>>();
        var truncated = false;

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (rows.Count == maxRows)
            {
                truncated = true;
                break;
            }

            var row = new object?[columns.Length];

            for (var i = 0; i < row.Length; i++)
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);

            rows.Add(row);
        }

        return new ResultSet(columns, rows, truncated);
    }

    /// <summary>
    /// Runs a batch through one command and reads every result set it produces.
    /// </summary>
    /// <remarks>
    /// A statement without rows gives a count, and a SELECT gives a set; both are
    /// answers. An engine error is a message: the sets read before it are kept,
    /// and the statements after it are not run. A cancellation is not a message.
    /// </remarks>
    private async Task<QueryBatchResult> RunBatchAsync(DbConnection connection, string sql, int maxRows, CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        var sets = new List<ResultSet>();
        var messages = new List<QueryMessage>();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        // The engine's own messages (PRINT, RAISE NOTICE) arrive while the batch runs,
        // so the listener is attached before it and removed after it.
        using var listener = ListenForMessages(command, messages.Add);

        return await RunCommandAsync<QueryBatchResult>(command, ct, async () =>
        {
            // The statement the reader is on, counted from zero. It moves on before the reader does, so an
            // error while advancing is attributed to the statement the reader was moving to.
            var statement = 0;

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                var reported = 0L;

                // RecordsAffected counts the whole batch so far, so a statement's
                // count is how much it grew since the last report. NextResult runs
                // a statement that returns no rows without exposing it as a set,
                // so the count is checked every time the reader moves, and once more at the end.
                void ReportChanges(bool always)
                {
                    // -1 means the engine did not count: an answer set, or a batch under SET NOCOUNT ON.
                    // That is unknown, so nothing is reported for it rather than a wrong count.
                    if (!ReportsRowCounts) return;

                    var total = reader.RecordsAffected;

                    if (total < 0 || (!always && total == reported)) return;

                    var rows = total - reported;

                    messages.Add(new QueryMessage(
                        Localizer.Get(rows == 1 ? StringKeys.DatabaseRowAffected : StringKeys.DatabaseRowsAffected, rows),
                        false,
                        null));
                    reported = total;
                }

                while (true)
                {
                    if (reader.FieldCount == 0)
                    {
                        ReportChanges(always: true);
                    }
                    else
                    {
                        ReportChanges(always: false);
                        sets.Add(await ReadSetAsync(reader, maxRows, ct).ConfigureAwait(false));
                    }

                    statement++;

                    if (!await reader.NextResultAsync(ct).ConfigureAwait(false)) break;
                }

                ReportChanges(always: false);
            }
            catch (DbException ex) when (!ct.IsCancellationRequested)
            {
                messages.Add(new QueryMessage(ex.Message, true, ErrorLine(ex, sql, statement)));
            }

            return new QueryBatchResult(sets, messages, started.Elapsed);
        }).ConfigureAwait(false);
    }

    private async Task<DataPage> ReadPageCoreAsync(DbConnection connection, DataPageRequest request, CancellationToken ct)
    {
        var details = await ReadDetailsAsync(connection, request.Table, ct).ConfigureAwait(false);

        var page = await ReadOneSetAsync(
            connection,
            Dialect.SelectPage(details, request.Offset, request.Limit, request.Sort, request.Filter),
            int.MaxValue,
            ct).ConfigureAwait(false);

        // The total is counted with the same filter, so the pager's last page is the filter's last page.
        var count = await QueryScalarAsync(
            connection,
            $"SELECT COUNT(*) FROM {Dialect.QualifiedName(details.Name)}{SqlFilter.Where(request.Filter)}",
            ct).ConfigureAwait(false);

        return new DataPage(details, page.Rows, Convert.ToInt64(count, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Applies the edits in one transaction. The first refused edit rolls the
    /// whole transaction back and is reported by index. A commit the engine
    /// refuses (a deferred constraint, say) rolls back the same way, and is
    /// reported with index -1, because no single edit is to blame.
    /// </summary>
    private async Task<int> ApplyEditsCoreAsync(DbConnection connection, DbObjectName name, IReadOnlyList<RowEdit> edits, CancellationToken ct)
    {
        var table = await ReadDetailsAsync(connection, name, ct).ConfigureAwait(false);

        if (!table.IsEditable)
            throw new InvalidOperationException(Localizer.Get(StringKeys.DatabaseNotEditable, name.ToString()));

        // A column that does not exist is a bug in the caller, so it is refused
        // before any statement runs, not reported as an edit the database refused.
        CheckEdits(table, edits);

        if (edits.Count == 0) return 0;

        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var changed = 0;

        // The edit being applied, or -1 once they are all applied and the commit is running.
        var position = -1;

        try
        {
            for (var index = 0; index < edits.Count; index++)
            {
                position = index;
                changed += await ApplyOneAsync(connection, transaction, table, edits[index], ct).ConfigureAwait(false);
            }

            // Not cancelled: a commit that has started finishes, or the engine is left with an unknown outcome.
            position = -1;
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return changed;
        }
        catch (Exception ex)
        {
            await RollbackAsync(transaction).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            throw position >= 0
                ? new DatabaseEditException(position, Localizer.Get(StringKeys.DatabaseEditFailed, position + 1, ex.Message), ex)
                : new DatabaseEditException(-1, Localizer.Get(StringKeys.DatabaseCommitFailed, ex.Message), ex);
        }
    }

    private async Task<int> ApplyOneAsync(
        DbConnection connection, DbTransaction transaction, TableDetails table, RowEdit edit, CancellationToken ct)
    {
        // An update with nothing to change says nothing about the row, so there is nothing to run.
        if (edit is RowUpdate { Changes.Count: 0 }) return 0;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var mustMatch = BuildStatement(command, table, edit);

        var changed = await RunCommandAsync<int>(command, ct, () => ExecuteEditAsync(command, ct)).ConfigureAwait(false);

        // An update or delete that finds nothing means the row changed or vanished
        // since the grid read it. Only zero is a miss: a trigger may change other
        // rows, and an engine may count more than one, so "exactly one" is too strict.
        // The count is the statement's own: ExecuteNonQuery's, or SQL Server's @@ROWCOUNT, which NOCOUNT does not hide.
        if (mustMatch && changed == 0)
            throw new InvalidOperationException(Localizer.Get(StringKeys.DatabaseRowVanished));

        // -1 is a count the engine did not give (a provider that does not report one): it is not taken as a miss
        // above, and adds nothing here, so the total is the rows the engine counted.
        return Math.Max(changed, 0);
    }

    /// <summary>
    /// Runs one edit statement and returns the rows it changed, or -1 when the
    /// engine does not say. ExecuteNonQuery by default; an engine whose count a
    /// session setting can hide reads it another way.
    /// </summary>
    protected virtual Task<int> ExecuteEditAsync(DbCommand command, CancellationToken ct) =>
        command.ExecuteNonQueryAsync(ct);

    /// <summary>Sets the command's text and parameters; returns whether the statement must match a row.</summary>
    private bool BuildStatement(DbCommand command, TableDetails table, RowEdit edit)
    {
        var target = Dialect.QualifiedName(table.Name);

        switch (edit)
        {
            case RowInsert insert:
            {
                var columns = new List<string>();
                var markers = new List<string>();

                foreach (var (name, text) in insert.Values)
                {
                    var column = FindColumn(table, name);
                    columns.Add(Dialect.Quote(column.Name));
                    markers.Add(AddParameter(command, ConvertValue(text, column), column));
                }

                command.CommandText = columns.Count == 0
                    ? $"INSERT INTO {target} {EmptyInsertClause}"
                    : $"INSERT INTO {target} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", markers)})";

                return false;
            }

            case RowUpdate update:
            {
                var assignments = new List<string>();

                foreach (var (name, text) in update.Changes)
                {
                    var column = FindColumn(table, name);
                    assignments.Add($"{Dialect.Quote(column.Name)} = {AddParameter(command, ConvertValue(text, column), column)}");
                }

                // The key is the row's original key, so an edit to a key column still finds the row.
                command.CommandText =
                    $"UPDATE {target} SET {string.Join(", ", assignments)} WHERE {KeyCondition(command, table, update.Key)}";

                return true;
            }

            case RowDelete delete:
                command.CommandText = $"DELETE FROM {target} WHERE {KeyCondition(command, table, delete.Key)}";
                return true;

            default:
                throw new ArgumentException($"Unknown edit {edit.GetType().Name}.");
        }
    }

    /// <summary>Every key column equal to its parameter; a NULL key column is tested with IS NULL.</summary>
    private string KeyCondition(DbCommand command, TableDetails table, IReadOnlyDictionary<string, object?> key)
    {
        var values = new Dictionary<string, object?>(key, StringComparer.OrdinalIgnoreCase);
        var terms = new List<string>();

        foreach (var column in table.KeyColumns)
        {
            var quoted = Dialect.Quote(column.Name);
            var value = values[column.Name];

            terms.Add(value is null ? $"{quoted} IS NULL" : $"{quoted} = {AddParameter(command, value, column)}");
        }

        return string.Join(" AND ", terms);
    }

    private string AddParameter(DbCommand command, object? value, ColumnInfo column)
    {
        var name = ParameterName(command.Parameters.Count);
        command.Parameters.Add(CreateParameter(command, name, value, column));
        return name;
    }

    private static void CheckEdits(TableDetails table, IReadOnlyList<RowEdit> edits)
    {
        foreach (var edit in edits)
        {
            switch (edit)
            {
                case RowInsert insert:
                    foreach (var name in insert.Values.Keys) FindColumn(table, name);
                    break;

                case RowUpdate update:
                    foreach (var name in update.Changes.Keys) FindColumn(table, name);
                    CheckKey(table, update.Key);
                    break;

                case RowDelete delete:
                    CheckKey(table, delete.Key);
                    break;

                default:
                    throw new ArgumentException($"Unknown edit {edit.GetType().Name}.");
            }
        }
    }

    private static void CheckKey(TableDetails table, IReadOnlyDictionary<string, object?> key)
    {
        foreach (var name in key.Keys) FindColumn(table, name);

        foreach (var column in table.KeyColumns)
        {
            if (!key.Keys.Any(k => string.Equals(k, column.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"The key of {table.Name} needs {column.Name}.");
        }
    }

    private static ColumnInfo FindColumn(TableDetails table, string name) =>
        table.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"{table.Name} has no column {name}.");

    /// <summary>
    /// A rollback that fails leaves the original failure as the one to report:
    /// the connection is closed on dispose, and the engine drops the open transaction with it.
    /// </summary>
    private static async Task RollbackAsync(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (DbException)
        {
        }
    }

    private static readonly string[] IsoDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
    ];

    /// <summary>The kind of value a column holds, as the conversion sees it. Engines read it through <see cref="ConvertValue"/>.</summary>
    protected enum ValueKind
    {
        Text,
        Integer,
        Decimal,
        Real,
        Boolean,
        Date,
        Guid,
        Binary,
    }

    /// <summary>
    /// The kind of value a declared type holds, from its name without the length:
    /// "varchar(20)" is text, "int" is an integer, "bit" is a boolean.
    /// </summary>
    protected static ValueKind KindOf(string dataType)
    {
        var stem = dataType.Split('(')[0].Trim().ToLowerInvariant();

        return stem switch
        {
            "integer" or "int" or "bigint" or "smallint" or "tinyint" or "mediumint" or "int2" or "int4" or "int8"
                or "serial" or "bigserial" or "smallserial" => ValueKind.Integer,
            "decimal" or "numeric" or "money" or "smallmoney" or "number" => ValueKind.Decimal,
            "real" or "float" or "double" or "double precision" or "float4" or "float8" => ValueKind.Real,
            "bool" or "boolean" or "bit" => ValueKind.Boolean,
            "date" or "datetime" or "datetime2" or "smalldatetime" or "timestamp" or "timestamptz"
                or "timestamp with time zone" or "timestamp without time zone" => ValueKind.Date,
            "uniqueidentifier" or "uuid" => ValueKind.Guid,
            "blob" or "binary" or "varbinary" or "bytea" or "image" or "longblob" or "mediumblob" or "tinyblob" => ValueKind.Binary,
            _ => ValueKind.Text,
        };
    }

    private static byte[]? ParseHex(string text)
    {
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            return Convert.FromHexString(text[2..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The refusal for a text that does not fit its column. Engines that convert their own kinds use it too.</summary>
    protected static FormatException BadValue(string text, ValueKind kind, ColumnInfo column) =>
        new(Localizer.Get(StringKeys.DatabaseBadValue, text, kind.ToString().ToLowerInvariant(), column.Name));
}
