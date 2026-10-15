using System.Collections.ObjectModel;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Data.Sessions;
using Basalt.Shell.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Basalt.Shell.Database;

/// <summary>
/// One row of the Database Explorer's tree.
/// </summary>
/// <remarks>
/// Children are filled when a node is expanded, not when the tree is built: a
/// connection reads its tables on expand, a table reads its columns, indexes
/// and keys on expand. Until then a node holds a single "Loading" line, which is
/// what gives it an expander to click.
/// </remarks>
public abstract class ExplorerNode : ObservableObject
{
    private string _text;
    private bool _isExpanded;

    protected ExplorerNode(string text) => _text = text;

    /// <summary>The icon beside the node, so the tree reads by shape before the eye reads the words.</summary>
    public virtual IconKind Icon => IconKind.None;

    /// <summary>
    /// Whether the node is open. The tree follows it both ways: a filter opens the groups that hold a match,
    /// and the user's own folding is kept when the list is rebuilt.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public string Text
    {
        get => _text;
        protected set => SetProperty(ref _text, value);
    }

    public ObservableCollection<ExplorerNode> Children { get; } = [];

    public override string ToString() => Text;

    /// <summary>Replaces the children with one line that says something rather than being a thing.</summary>
    protected void ShowMessage(string message, bool isError = false)
    {
        Children.Clear();
        Children.Add(new MessageNode(message, isError));
    }
}

/// <summary>
/// A line of text with nothing under it: loading, nothing found, a failure. A failure wears the error
/// icon and the rest the information one, so a red cross is never mistaken for "still loading".
/// </summary>
public sealed class MessageNode(string text, bool isError = false) : ExplorerNode(text)
{
    public bool IsError { get; } = isError;

    public override IconKind Icon => IsError ? IconKind.Error : IconKind.Information;
}

/// <summary>A column, index or key: a line with nothing under it.</summary>
public sealed class LeafNode(string text, IconKind icon = IconKind.Column) : ExplorerNode(text)
{
    public override IconKind Icon { get; } = icon;
}

/// <summary>How the filter box matches an object's name.</summary>
public static class ExplorerFilter
{
    /// <summary>Whether a name contains the text typed, ignoring case. An empty filter matches everything.</summary>
    public static bool Matches(string name, string filter)
    {
        var wanted = filter.Trim();

        return wanted.Length == 0 || name.Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A saved connection: its tables and views appear under it once it is open.</summary>
public sealed class ConnectionNode : ExplorerNode
{
    public ConnectionNode(DatabaseConnectionSettings saved)
        : base(saved.Name)
    {
        Saved = saved;
        Reset();
    }

    public DatabaseConnectionSettings Saved { get; }

    public string ConnectionId => Saved.Id;

    /// <summary>Each engine has its own mark on the connection's cylinder.</summary>
    public override IconKind Icon => DatabaseKinds.Parse(Saved.Kind) switch
    {
        DatabaseKind.Sqlite => IconKind.DatabaseSqlite,
        DatabaseKind.SqlServer => IconKind.DatabaseSqlServer,
        DatabaseKind.PostgreSql => IconKind.DatabasePostgreSql,
        DatabaseKind.MySql => IconKind.DatabaseMySql,
        _ => IconKind.DatabaseSqlite,
    };

    private readonly List<GroupNode> _groups = [];

    /// <summary>The filter box's text, applied to the objects whenever they are listed.</summary>
    public string Filter { get; private set; } = "";

    /// <summary>Whether the object list has been read since the last reset.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The engine's name, shown as a tooltip because the connection's own name does not say it.</summary>
    public string KindLabel => DatabaseKinds.Label(DatabaseKinds.Parse(Saved.Kind));

    /// <summary>Puts the node back to the state before anything was read.</summary>
    public void Reset()
    {
        IsLoaded = false;
        _groups.Clear();
        ShowMessage(Localizer.Get(StringKeys.DbNotConnected));
    }

    public void ShowLoading() => ShowMessage(Localizer.Get(StringKeys.DbLoading));

    /// <summary>Says why the connection could not be read, in place of its tree.</summary>
    public void ShowFailure(string message)
    {
        IsLoaded = false;
        _groups.Clear();
        ShowMessage(message, isError: true);
    }

    /// <summary>Reads the tables and views of an open session into the tree.</summary>
    public async Task LoadAsync(IDatabaseSession session, CancellationToken ct)
    {
        var objects = await session.GetObjectsAsync(ct).ConfigureAwait(true);

        var tables = objects.Where(o => o.Kind == DbObjectKind.Table).Select(o => new ObjectNode(o, session)).ToList();
        var views = objects.Where(o => o.Kind == DbObjectKind.View).Select(o => new ObjectNode(o, session)).ToList();

        _groups.Clear();
        _groups.Add(new GroupNode(Localizer.Get(StringKeys.DbTablesGroup), tables, IconKind.FolderTables));
        _groups.Add(new GroupNode(Localizer.Get(StringKeys.DbViewsGroup), views, IconKind.FolderViews));

        IsLoaded = true;
        ShowGroups();
    }

    /// <summary>
    /// Narrows the objects to those whose name contains the text. A group keeps its place only while it
    /// holds a match, and opens to show it; the connection stays, so the tree never loses its roots.
    /// </summary>
    public void ApplyFilter(string filter)
    {
        Filter = filter.Trim();

        if (IsLoaded) ShowGroups();
    }

    private void ShowGroups()
    {
        var filtering = Filter.Length > 0;

        foreach (var group in _groups) group.ApplyFilter(Filter);

        var shown = _groups.Where(g => !filtering || g.VisibleCount > 0).ToList();

        Children.Clear();

        if (shown.Count == 0)
        {
            Children.Add(new MessageNode(Localizer.Get(StringKeys.DbFilterNoMatch, Filter)));
            return;
        }

        foreach (var group in shown)
        {
            if (filtering) group.IsExpanded = true;

            Children.Add(group);
        }
    }
}

/// <summary>"Tables" or "Views" under a connection, with the count in its title.</summary>
public sealed class GroupNode : ExplorerNode
{
    private readonly string _title;
    private readonly IReadOnlyList<ObjectNode> _items;

    public GroupNode(string title, IReadOnlyList<ObjectNode> items, IconKind icon = IconKind.Folder)
        : base($"{title} ({items.Count})")
    {
        _title = title;
        _items = items;
        Icon = icon;

        foreach (var item in items) Children.Add(item);
    }

    public override IconKind Icon { get; }

    /// <summary>How many objects the filter lets through.</summary>
    public int VisibleCount => Children.Count;

    /// <summary>Shows the objects whose name matches, and says how many of the whole that is.</summary>
    public void ApplyFilter(string filter)
    {
        var matching = _items.Where(item => ExplorerFilter.Matches(item.Name.ToString(), filter)).ToList();

        Children.Clear();

        foreach (var item in matching) Children.Add(item);

        Text = filter.Trim().Length == 0
            ? $"{_title} ({_items.Count})"
            : Localizer.Get(StringKeys.DbFilteredGroup, _title, matching.Count, _items.Count);
    }
}

/// <summary>A table or a view. Its columns, indexes and keys are read on expand.</summary>
public sealed class ObjectNode : ExplorerNode
{
    private readonly IDatabaseSession _session;
    private readonly CategoryNode _columns = new(StringKeys.DbColumnsGroup);
    private readonly CategoryNode _indexes = new(StringKeys.DbIndexesGroup);
    private readonly CategoryNode _keys = new(StringKeys.DbForeignKeysGroup);

    public ObjectNode(DbObjectInfo info, IDatabaseSession session)
        : base(info.Name.Name)
    {
        Info = info;
        _session = session;

        Children.Add(_columns);
        Children.Add(_indexes);
        Children.Add(_keys);
    }

    public DbObjectInfo Info { get; }

    public override IconKind Icon => IsView ? IconKind.View : IconKind.Table;

    public DbObjectName Name => Info.Name;

    public bool IsView => Info.Kind == DbObjectKind.View;

    /// <summary>The session this object was listed through, for the actions that need an open database.</summary>
    public IDatabaseSession Session => _session;

    /// <summary>Whether the columns, indexes and keys have been read.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Forgets the details read so far, so the next expand reads them again.</summary>
    public void ForgetDetails() => IsLoaded = false;

    private Task? _loading;

    /// <summary>
    /// Reads the details once and fills the three categories. A failure is shown in them, not thrown. A second
    /// call while the first is still reading (a node folded and opened again, Refresh pressed meanwhile) joins
    /// it instead of asking the database twice.
    /// </summary>
    public Task LoadDetailsAsync(CancellationToken ct)
    {
        if (IsLoaded) return Task.CompletedTask;
        if (_loading is { IsCompleted: false } running) return running;

        return _loading = ReadDetailsAsync(ct);
    }

    private async Task ReadDetailsAsync(CancellationToken ct)
    {
        _columns.ShowLoading();
        _indexes.ShowLoading();
        _keys.ShowLoading();

        try
        {
            var details = await _session.GetDetailsAsync(Info.Name, ct).ConfigureAwait(true);

            var foreignColumns = details.ForeignKeys
                .SelectMany(k => k.Columns)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _columns.Fill(details.Columns.Select(c => new LeafNode(ColumnLabel(c, foreignColumns.Contains(c.Name)), ColumnIcon(c, foreignColumns.Contains(c.Name)))));
            _indexes.Fill(details.Indexes.Select(i => new LeafNode(IndexLabel(i), IconKind.Index)));
            _keys.Fill(details.ForeignKeys.Select(k => new LeafNode(KeyLabel(k), IconKind.ForeignKeyColumn)));

            IsLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // Stopped (the tree was rebuilt, the connection closed): back to unread, so the next expand reads it.
            _columns.Reset();
            _indexes.Reset();
            _keys.Reset();
        }
        catch (Exception ex)
        {
            _columns.ShowFailure(ex.Message);
            _indexes.ShowFailure(ex.Message);
            _keys.ShowFailure(ex.Message);
        }
    }

    /// <summary>The icon of a column: a key when it identifies the row, a chain link when it points at another table.</summary>
    private static IconKind ColumnIcon(ColumnInfo column, bool isForeignKey) =>
        column.IsPrimaryKey ? IconKind.PrimaryKeyColumn
        : isForeignKey ? IconKind.ForeignKeyColumn
        : IconKind.Column;

    /// <summary>The label of a column: its name, type, and the marks for key and nullability.</summary>
    internal static string ColumnLabel(ColumnInfo column, bool isForeignKey)
    {
        var marks = new List<string>();

        if (column.IsPrimaryKey) marks.Add(Localizer.Get(StringKeys.DbPrimaryKeyMark));
        if (isForeignKey) marks.Add(Localizer.Get(StringKeys.DbForeignKeyMark));
        marks.Add(Localizer.Get(column.IsNullable ? StringKeys.DbNullableMark : StringKeys.DbNotNullMark));

        return $"{column.Name}  {column.DataType}  {string.Join("  ", marks)}";
    }

    private static string IndexLabel(IndexInfo index)
    {
        var columns = string.Join(", ", index.Columns);

        if (index.IsPrimaryKey) return $"{index.Name} ({columns})  {Localizer.Get(StringKeys.DbPrimaryKeyIndexMark)}";
        if (index.IsUnique) return $"{index.Name} ({columns})  {Localizer.Get(StringKeys.DbUniqueMark)}";

        return $"{index.Name} ({columns})";
    }

    private static string KeyLabel(ForeignKeyInfo key) =>
        Localizer.Get(StringKeys.DbForeignKeyLabel,
            string.Join(", ", key.Columns),
            key.ReferencedTable.ToString(),
            string.Join(", ", key.ReferencedColumns));
}

/// <summary>"Columns", "Indexes" or "Foreign Keys" under a table, with the count once known.</summary>
public sealed class CategoryNode : ExplorerNode
{
    private readonly string _title;

    public CategoryNode(string titleKey)
        : base(Localizer.Get(titleKey))
    {
        _title = Text;
    }

    public override IconKind Icon => IconKind.Folder;

    public void ShowLoading() => ShowMessage(Localizer.Get(StringKeys.DbLoading));

    public void ShowFailure(string message) => ShowMessage(message, isError: true);

    /// <summary>Back to the unread state: the bare title, nothing under it.</summary>
    public void Reset()
    {
        Text = _title;
        Children.Clear();
    }

    public void Fill(IEnumerable<LeafNode> leaves)
    {
        var list = leaves.ToList();

        Text = $"{_title} ({list.Count})";
        Children.Clear();

        foreach (var leaf in list) Children.Add(leaf);
    }
}
