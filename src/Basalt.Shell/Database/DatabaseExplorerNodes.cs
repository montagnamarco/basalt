using System.Collections.ObjectModel;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Data.Sessions;
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

    protected ExplorerNode(string text) => _text = text;

    public string Text
    {
        get => _text;
        protected set => SetProperty(ref _text, value);
    }

    public ObservableCollection<ExplorerNode> Children { get; } = [];

    public override string ToString() => Text;

    /// <summary>Replaces the children with one line that says something rather than being a thing.</summary>
    protected void ShowMessage(string message)
    {
        Children.Clear();
        Children.Add(new MessageNode(message));
    }
}

/// <summary>A line of text with nothing under it: loading, nothing found, a failure.</summary>
public sealed class MessageNode(string text) : ExplorerNode(text);

/// <summary>A column, index or key: a line with nothing under it.</summary>
public sealed class LeafNode(string text) : ExplorerNode(text);

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

    /// <summary>Whether the object list has been read since the last reset.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The engine's name, shown as a tooltip because the connection's own name does not say it.</summary>
    public string KindLabel => DatabaseKinds.Label(DatabaseKinds.Parse(Saved.Kind));

    /// <summary>Puts the node back to the state before anything was read.</summary>
    public void Reset()
    {
        IsLoaded = false;
        ShowMessage(Localizer.Get(StringKeys.DbNotConnected));
    }

    public void ShowLoading() => ShowMessage(Localizer.Get(StringKeys.DbLoading));

    /// <summary>Says why the connection could not be read, in place of its tree.</summary>
    public void ShowFailure(string message)
    {
        IsLoaded = false;
        ShowMessage(message);
    }

    /// <summary>Reads the tables and views of an open session into the tree.</summary>
    public async Task LoadAsync(IDatabaseSession session, CancellationToken ct)
    {
        var objects = await session.GetObjectsAsync(ct).ConfigureAwait(true);

        var tables = objects.Where(o => o.Kind == DbObjectKind.Table).Select(o => new ObjectNode(o, session)).ToList();
        var views = objects.Where(o => o.Kind == DbObjectKind.View).Select(o => new ObjectNode(o, session)).ToList();

        Children.Clear();
        Children.Add(new GroupNode(Localizer.Get(StringKeys.DbTablesGroup), tables));
        Children.Add(new GroupNode(Localizer.Get(StringKeys.DbViewsGroup), views));

        IsLoaded = true;
    }
}

/// <summary>"Tables" or "Views" under a connection, with the count in its title.</summary>
public sealed class GroupNode : ExplorerNode
{
    public GroupNode(string title, IReadOnlyList<ObjectNode> items)
        : base($"{title} ({items.Count})")
    {
        foreach (var item in items) Children.Add(item);
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

    public DbObjectName Name => Info.Name;

    public bool IsView => Info.Kind == DbObjectKind.View;

    /// <summary>The session this object was listed through, for the actions that need an open database.</summary>
    public IDatabaseSession Session => _session;

    /// <summary>Whether the columns, indexes and keys have been read.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Forgets the details read so far, so the next expand reads them again.</summary>
    public void ForgetDetails() => IsLoaded = false;

    /// <summary>Reads the details once and fills the three categories. A failure is shown in them, not thrown.</summary>
    public async Task LoadDetailsAsync(CancellationToken ct)
    {
        if (IsLoaded) return;

        _columns.ShowLoading();
        _indexes.ShowLoading();
        _keys.ShowLoading();

        try
        {
            var details = await _session.GetDetailsAsync(Info.Name, ct).ConfigureAwait(true);

            _columns.Fill(details.Columns.Select(ColumnLabel));
            _indexes.Fill(details.Indexes.Select(IndexLabel));
            _keys.Fill(details.ForeignKeys.Select(KeyLabel));

            IsLoaded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _columns.ShowFailure(ex.Message);
            _indexes.ShowFailure(ex.Message);
            _keys.ShowFailure(ex.Message);
        }
    }

    /// <summary>The label of a column: its name, type, and the marks for key and nullability.</summary>
    private static string ColumnLabel(ColumnInfo column)
    {
        var marks = new List<string>();

        if (column.IsPrimaryKey) marks.Add(Localizer.Get(StringKeys.DbPrimaryKeyMark));
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

    public void ShowLoading() => ShowMessage(Localizer.Get(StringKeys.DbLoading));

    public void ShowFailure(string message) => ShowMessage(message);

    public void Fill(IEnumerable<string> labels)
    {
        var list = labels.ToList();

        Text = $"{_title} ({list.Count})";
        Children.Clear();

        foreach (var label in list) Children.Add(new LeafNode(label));
    }
}
