using Basalt.Core.Localization;

namespace Basalt.Data.Sessions;

/// <summary>
/// Reads a table into a design, and runs a design's script against a session.
/// </summary>
/// <remarks>
/// Every call goes through the session, which runs it on its own worker thread, so none of this touches the
/// interface thread. The script is run statement by statement inside an explicit transaction rather than as one
/// batch: a batch stops at its first error but leaves the transaction it opened still open.
/// </remarks>
public static class TableDesignRunner
{
    /// <summary>The table's design, including what SQLite's rebuild must carry over and the catalogue does not report.</summary>
    public static async Task<TableDesign> LoadAsync(IDatabaseSession session, DbObjectName name, CancellationToken ct = default)
    {
        var details = await session.GetDetailsAsync(name, ct).ConfigureAwait(false);
        var design = TableDesign.FromDetails(details);

        if (session.Kind == DatabaseKind.MySql) return await ReadMySqlExtrasAsync(session, details, design, ct).ConfigureAwait(false);

        if (session.Kind != DatabaseKind.Sqlite) return design;

        var literal = TableText.Literal(details.Name.Name);

        var source = await QueryAsync(session, $"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = {literal}", ct).ConfigureAwait(false);
        var triggers = await QueryAsync(session, $"SELECT sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = {literal} AND sql IS NOT NULL", ct).ConfigureAwait(false);
        var keys = await QueryAsync(session, $"PRAGMA foreign_key_list({session.Dialect.Quote(details.Name.Name)})", ct).ConfigureAwait(false);

        // The catalogue model has no ON DELETE / ON UPDATE; a rebuild that dropped them would change what deleting a parent does.
        var actions = keys.Rows
            .GroupBy(r => Convert.ToInt64(r[0], System.Globalization.CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => (OnUpdate: g.First()[5] as string, OnDelete: g.First()[6] as string));

        // The catalogue lists the keys in the order of foreign_key_list's ids, so the nth key has the nth id.
        var ids = keys.Rows.Select(r => Convert.ToInt64(r[0], System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order().ToList();

        var createSql = source.Rows.FirstOrDefault()?[0] as string;

        // PRAGMA foreign_key_list does not say DEFERRABLE; the CREATE statement does, by the table each key refers to.
        var deferrable = createSql is null ? [] : SqliteSchemaSql.DeferrableByParent(createSql);

        var foreign = design.ForeignKeys
            .Select((key, index) =>
            {
                var withActions = actions.TryGetValue(ids.ElementAtOrDefault(index), out var action)
                    ? key with { OnDelete = action.OnDelete, OnUpdate = action.OnUpdate }
                    : key;

                if (!deferrable.TryGetValue(key.ReferencedTable.Name, out var clauses)) return withActions;

                return withActions with
                {
                    Deferrable = clauses.Count == 1
                        ? clauses[0]
                        : clauses.Any(c => c is not null) ? DesignForeignKey.AmbiguousDeferrable : null,
                };
            })
            .ToList();

        // The statement of each index keeps its WHERE, DESC and COLLATE, which index_info does not report.
        var indexRows = await QueryAsync(session, $"SELECT name, sql FROM sqlite_master WHERE type = 'index' AND tbl_name = {literal} AND sql IS NOT NULL", ct).ConfigureAwait(false);
        var indexSql = indexRows.Rows.Where(r => r[0] is string && r[1] is string).ToDictionary(r => (string)r[0]!, r => (string)r[1]!);

        return design with
        {
            ForeignKeys = foreign,
            Triggers = [.. triggers.Rows.Select(r => r[0] as string).OfType<string>()],
            SourceSql = createSql,
            IndexSql = indexSql,
        };
    }

    /// <summary>
    /// MySQL keeps a column's character set, collation, comment and ON UPDATE clause out of what the catalogue model
    /// reports, and MODIFY would reset them; they are read here so the script can write them back.
    /// </summary>
    private static async Task<TableDesign> ReadMySqlExtrasAsync(IDatabaseSession session, TableDetails details, TableDesign design, CancellationToken ct)
    {
        var rows = await QueryAsync(
            session,
            "SELECT COLUMN_NAME, CHARACTER_SET_NAME, COLLATION_NAME, COLUMN_COMMENT, EXTRA FROM information_schema.COLUMNS " +
            $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = {TableText.Literal(details.Name.Name)}",
            ct).ConfigureAwait(false);

        var extras = rows.Rows.ToDictionary(
            r => r[0] as string ?? "",
            r =>
            {
                var onUpdate = System.Text.RegularExpressions.Regex.Match(r[4] as string ?? "", @"on update\s+(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                return new ColumnExtras(
                    r[1] as string,
                    r[2] as string,
                    r[3] is string { Length: > 0 } comment ? comment : null,
                    onUpdate.Success ? onUpdate.Groups[1].Value : null);
            },
            StringComparer.OrdinalIgnoreCase);

        return design with
        {
            Columns = [.. design.Columns.Select(c => extras.TryGetValue(c.Name, out var found) ? c with { Extras = found } : c)],
        };
    }

    /// <summary>
    /// Whether the table in the database still has the columns, indexes, keys and triggers a design was loaded with.
    /// A script is written from that design, so one run over a table somebody else changed meanwhile could drop or
    /// overwrite what the designer never saw.
    /// </summary>
    public static async Task<bool> IsUnchangedAsync(IDatabaseSession session, TableDesign loaded, CancellationToken ct = default)
    {
        var current = await LoadAsync(session, loaded.Name, ct).ConfigureAwait(false);

        return loaded.SameStructureAs(current);
    }

    /// <summary>
    /// Runs a script: the statements before the transaction, the transaction with its statements and foreign key
    /// check, and the statements after it, which run even when the script failed. A failure rolls the
    /// transaction back and is thrown with the engine's message.
    /// </summary>
    public static async Task ApplyAsync(IDatabaseSession session, TableScript script, CancellationToken ct = default)
    {
        if (script.IsEmpty) return;

        // A setting the script changes is read first, so it is put back to what it was and not to what is usual.
        string? restore = null;

        try
        {
            if (script.SettingQuery is not null && script.SettingRestore is not null)
            {
                var read = await session.ExecuteAsync(script.SettingQuery, 1, ct).ConfigureAwait(false);
                Throw(read);

                if (read.ResultSets.FirstOrDefault()?.Rows.FirstOrDefault()?.FirstOrDefault() is { } value)
                    restore = string.Format(System.Globalization.CultureInfo.InvariantCulture, script.SettingRestore, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
            }

            foreach (var statement in script.Before)
                await RunAsync(session, statement, ct).ConfigureAwait(false);

            if (script.Transaction is not null)
                await RunAsync(session, script.Transaction.Begin, ct).ConfigureAwait(false);

            try
            {
                foreach (var statement in script.Statements)
                    await RunAsync(session, statement, ct).ConfigureAwait(false);

                if (script.ForeignKeyCheck is not null)
                {
                    var check = await session.ExecuteAsync(script.ForeignKeyCheck, 1000, ct).ConfigureAwait(false);
                    Throw(check);

                    var violations = check.ResultSets.Sum(s => s.Rows.Count);

                    if (violations > 0)
                        throw new InvalidOperationException(Localizer.Get(StringKeys.TableDesignerForeignKeyViolation, violations));
                }

                // A commit that has started finishes: cancelling it would leave the outcome unknown.
                if (script.Transaction is not null)
                    await RunAsync(session, script.Transaction.Commit, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (script.Transaction is not null)
                    await TryRollbackAsync(session, script.Transaction.Rollback).ConfigureAwait(false);

                throw;
            }
        }
        finally
        {
            foreach (var statement in restore is null ? script.After : [restore])
                await TryRunAsync(session, statement).ConfigureAwait(false);
        }
    }

    private static async Task RunAsync(IDatabaseSession session, string sql, CancellationToken ct) =>
        Throw(await session.ExecuteAsync(sql, 1000, ct).ConfigureAwait(false));

    private static async Task TryRunAsync(IDatabaseSession session, string sql)
    {
        try
        {
            await session.ExecuteAsync(sql, 1000, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Restoring a setting must not hide the error that is being thrown.
        }
    }

    private static Task TryRollbackAsync(IDatabaseSession session, string rollback) => TryRunAsync(session, rollback);

    private static void Throw(QueryBatchResult result)
    {
        if (result.Failed)
            throw new InvalidOperationException(result.Messages.First(m => m.IsError).Text);
    }

    private static async Task<ResultSet> QueryAsync(IDatabaseSession session, string sql, CancellationToken ct)
    {
        var result = await session.ExecuteAsync(sql, int.MaxValue, ct).ConfigureAwait(false);

        Throw(result);

        return result.ResultSets.FirstOrDefault() ?? new ResultSet([], [], false);
    }
}
