using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Basalt.Core.Localization;
using Basalt.Data.Sessions;
using Basalt.Shell.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Basalt.Shell.Database;

/// <summary>What the user decided when shown the script a save would run.</summary>
public enum ScriptChoice
{
    Cancel,
    Run,
    ScriptOnly,
}

/// <summary>One column of the designer's grid, as the user is editing it.</summary>
internal sealed class DesignRow : ObservableObject
{
    private string _name;
    private string _dataType;
    private bool _isNullable;
    private bool _isPrimaryKey;
    private string _defaultValue;
    private bool _isIdentity;

    public DesignRow(DesignColumn column)
    {
        _name = column.Name;
        _dataType = column.DataType;
        _isNullable = column.IsNullable;
        _isPrimaryKey = column.IsPrimaryKey;
        _defaultValue = column.DefaultValue ?? "";
        _isIdentity = column.IsIdentity;
        OriginalName = column.OriginalName;
        Extras = column.Extras;
    }

    /// <summary>What the engine keeps on the column that the grid does not edit (MySQL's collation, comment, ON UPDATE); passed through unchanged.</summary>
    public ColumnExtras? Extras { get; }

    /// <summary>The column's name in the stored table; null for a column added here.</summary>
    public string? OriginalName { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? "");
    }

    public string DataType
    {
        get => _dataType;
        set => SetProperty(ref _dataType, value ?? "");
    }

    public bool IsNullable
    {
        get => _isNullable;
        set => SetProperty(ref _isNullable, value);
    }

    public bool IsPrimaryKey
    {
        get => _isPrimaryKey;
        set
        {
            if (!SetProperty(ref _isPrimaryKey, value)) return;

            // A key column cannot hold NULL.
            if (value) IsNullable = false;
        }
    }

    public string DefaultValue
    {
        get => _defaultValue;
        set => SetProperty(ref _defaultValue, value ?? "");
    }

    public bool IsIdentity
    {
        get => _isIdentity;
        set => SetProperty(ref _isIdentity, value);
    }

    public DesignColumn ToColumn() => new(
        Name.Trim(),
        DataType.Trim(),
        IsNullable,
        IsPrimaryKey,
        string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue.Trim(),
        IsIdentity,
        OriginalName,
        Extras);
}

/// <summary>
/// The table designer: a grid of columns for a new or an existing table, and Save, which shows the script the
/// change needs before running it.
/// </summary>
/// <remarks>
/// Nothing here talks to a database on the interface thread: reading the table and running the script are
/// <see cref="TableDesignRunner"/> calls on the session's own worker. The script is written from the difference
/// between the table as it was loaded and the grid as it is, so a column left alone is not mentioned. After a
/// save the table is read again, so the next edit is a difference from what the database now holds.
/// </remarks>
public sealed class TableDesignerView : Grid, IDatabaseDocumentContent
{
    private readonly DatabaseWorkspace _workspace;
    private readonly ObservableCollection<DesignRow> _rows = [];
    private readonly DataGrid _grid;
    private readonly TextBox _name;
    private readonly TextBlock _notice;
    private readonly TextBlock _status;
    private readonly Button _save;
    private readonly Button _remove;

    private DbObjectName? _existing;
    private string? _schema;
    private TableDesign? _original;
    private TableScriptGenerator? _generator;
    private DatabaseKind _kind;
    private List<DesignColumn> _baseline = [];
    private string _baselineName = "";
    private bool _loaded;
    private bool _saving;

    public TableDesignerView(DatabaseWorkspace workspace, string connectionId, DbObjectName? existing)
    {
        _workspace = workspace;
        ConnectionId = connectionId;
        _existing = existing;

        _name = new TextBox
        {
            Name = "TableDesignerName",
            FontSize = PanelMetrics.FontSize,
            MinWidth = 220,
            IsReadOnly = existing is not null,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var add = Button(IconKind.RowAdd, StringKeys.TableDesignerAddColumn, AddColumn);
        _remove = Button(IconKind.RowDelete, StringKeys.TableDesignerRemoveColumn, RemoveSelected);
        _save = Button(IconKind.Save, StringKeys.TableDesignerSave, () => _ = SaveAsync());
        var revert = Button(IconKind.Undo, StringKeys.TableDesignerRevert, Revert);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Normal,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            Children =
            {
                new TextBlock
                {
                    Text = Localizer.Get(StringKeys.TableDesignerTableName),
                    FontSize = PanelMetrics.FontSize,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _name,
                add,
                _remove,
                _save,
                revert,
            },
        };

        _grid = new DataGrid
        {
            Name = "TableDesignerGrid",
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserResizeColumns = true,
            FontSize = PanelMetrics.FontSize,
        };

        DatabaseGrids.StyleHeaders(_grid);

        _notice = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        }.Themed(TextBlock.ForegroundProperty, "ErrorForegroundBrush");

        _status = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            TextWrapping = TextWrapping.Wrap,
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");

        Place(toolbar, 0);
        Place(_notice, 1);
        Place(_grid, 2);
        Place(_status, 3);
    }

    public string ConnectionId { get; }

    /// <summary>The stored table this designer edits, or null while it designs one that does not exist yet.</summary>
    public DbObjectName? Table => _existing;

    /// <summary>The tab's title.</summary>
    public string Title => _existing is { } name
        ? Localizer.Get(StringKeys.TableDesignerTitle, name.ToString())
        : Localizer.Get(StringKeys.TableDesignerNewTitle);

    /// <summary>Raised when the title changes, which it does once a new table has been created.</summary>
    public event EventHandler? TitleChanged;

    /// <summary>Raised with the script text when the user chose "Generate script only".</summary>
    public event EventHandler<string>? ScriptOnlyRequested;

    /// <summary>Raised after a script ran, so the tree can list the table again.</summary>
    public event EventHandler? Saved;

    /// <summary>Replaces the dialog that shows the script. For the tests, which choose as a user would.</summary>
    internal Func<TableScript, Task<ScriptChoice>>? ScriptReviewForTests { get; set; }

    /// <summary>The columns as the grid holds them, in order. For the tests.</summary>
    internal IReadOnlyList<DesignRow> Rows => _rows;

    internal DataGrid Grid => _grid;

    internal TextBox NameBox => _name;

    /// <summary>The generator of the connection's engine; null until the table is loaded.</summary>
    internal TableScriptGenerator? Generator => _generator;

    internal string NoticeText => _notice.IsVisible ? _notice.Text ?? "" : "";

    internal string StatusText => _status.Text ?? "";

    /// <summary>Whether the grid differs from what was loaded or last saved.</summary>
    public bool HasUnsavedChanges => _loaded
        && (_name.Text?.Trim() != _baselineName || !_rows.Select(r => r.ToColumn()).SequenceEqual(_baseline));

    /// <summary>Reads the table (or prepares a new one) and fills the grid.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var session = await _workspace.OpenAsync(ConnectionId, ct).ConfigureAwait(true);

            _kind = session.Kind;
            _generator = TableScriptGenerator.For(_kind);
            BuildColumns();

            if (_existing is { } name)
            {
                _original = await TableDesignRunner.LoadAsync(session, name, ct).ConfigureAwait(true);
                Fill(_original);
            }
            else
            {
                _schema = _kind switch
                {
                    DatabaseKind.SqlServer => "dbo",
                    DatabaseKind.PostgreSql => "public",
                    _ => null,
                };

                var first = new DesignColumn(
                    Localizer.Get(StringKeys.TableDesignerNewColumn, 1), _generator.CommonTypes[0], true, false, null, false);

                Fill(TableDesign.Empty(new DbObjectName(_schema, Localizer.Get(StringKeys.TableDesignerNewTableName))) with { Columns = [first] });
            }

            _loaded = true;
            _status.Text = "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _notice.Text = Localizer.Get(StringKeys.TableDesignerLoadFailed, _existing?.ToString() ?? "", ex.Message);
            _notice.IsVisible = true;
        }
    }

    private void Fill(TableDesign design)
    {
        _schema = design.Name.Schema;
        _name.Text = design.Name.Name;
        _rows.Clear();

        foreach (var column in design.Columns) _rows.Add(new DesignRow(column));

        _baseline = [.. design.Columns];
        _baselineName = design.Name.Name;
    }

    /// <summary>The design as the grid holds it now.</summary>
    internal TableDesign CurrentDesign() => new(
        new DbObjectName(_schema, (_name.Text ?? "").Trim()),
        [.. _rows.Select(r => r.ToColumn())],
        _original?.Indexes ?? [],
        _original?.ForeignKeys ?? [],
        _original?.Triggers ?? [],
        _original?.SourceSql);

    /// <summary>The script a save would run now, or null while the table is not loaded or the design is invalid (the reason is shown).</summary>
    internal TableScript? CurrentScript()
    {
        if (_generator is null) return null;

        var edited = CurrentDesign();
        var issues = edited.Validate(_kind);

        if (issues.Count > 0)
        {
            _notice.Text = string.Join(" ", issues);
            _notice.IsVisible = true;
            return null;
        }

        _notice.IsVisible = false;

        return _generator.Script(_original, edited);
    }

    /// <summary>
    /// Shows the script and, if the user agrees, runs it. Returns true when the table now matches the grid
    /// (or already did), false when nothing was saved: a refused design, a cancel, "script only", or a failure.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (_saving || !_loaded) return false;

        var script = CurrentScript();

        if (script is null) return false;

        if (script.Refusal is { } refusal)
        {
            _notice.Text = refusal;
            _notice.IsVisible = true;

            return false;
        }

        if (script.IsEmpty)
        {
            _status.Text = Localizer.Get(StringKeys.TableDesignerNoChanges);
            return true;
        }

        var choice = await (ScriptReviewForTests?.Invoke(script) ?? ReviewAsync(script)).ConfigureAwait(true);

        if (choice == ScriptChoice.Cancel) return false;

        if (choice == ScriptChoice.ScriptOnly)
        {
            ScriptOnlyRequested?.Invoke(this, script.Text);
            return false;
        }

        _saving = true;
        var edited = CurrentDesign();

        try
        {
            var session = await _workspace.OpenAsync(ConnectionId).ConfigureAwait(true);

            // The check and the apply are separate session calls: IDatabaseSession has no way to hold its one-operation
            // gate across two, so a change made by another connection in the few milliseconds between them is not
            // seen. The check shortens that window from the whole time the dialog was open; it cannot close it.
            // The script was written from the table as it was loaded. If it is not that table any more, it is not run:
            // it could drop or overwrite what changed meanwhile and what the designer never showed.
            if (_original is not null && !await TableDesignRunner.IsUnchangedAsync(session, _original).ConfigureAwait(true))
            {
                _notice.Text = Localizer.Get(StringKeys.TableDesignerStale, edited.Name.ToString());
                _notice.IsVisible = true;

                return false;
            }

            await TableDesignRunner.ApplyAsync(session, script).ConfigureAwait(true);

            // Read back, so the next save is a difference from what the database now holds.
            _original = await TableDesignRunner.LoadAsync(session, edited.Name).ConfigureAwait(true);

            var created = _existing is null;

            _existing = edited.Name;
            _name.IsReadOnly = true;
            Fill(_original);

            _notice.IsVisible = false;
            _status.Text = Localizer.Get(StringKeys.TableDesignerSaved, edited.Name.ToString());

            if (created) TitleChanged?.Invoke(this, EventArgs.Empty);

            Saved?.Invoke(this, EventArgs.Empty);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _notice.Text = Localizer.Get(StringKeys.TableDesignerSaveFailed, edited.Name.ToString(), ex.Message);
            _notice.IsVisible = true;

            return false;
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Called by Ctrl+S in the tab.</summary>
    internal void SaveFromKeyboard() => _ = SaveAsync();

    private async Task<ScriptChoice> ReviewAsync(TableScript script)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return ScriptChoice.Cancel;

        return await TableScriptDialog.AskAsync(owner, Title, script).ConfigureAwait(true);
    }

    private void AddColumn()
    {
        var number = _rows.Count + 1;
        var type = _generator?.CommonTypes[0] ?? "";

        var row = new DesignRow(new DesignColumn(Localizer.Get(StringKeys.TableDesignerNewColumn, number), type, true, false, null, false));

        _rows.Add(row);
        _grid.SelectedItem = row;
    }

    private void RemoveSelected()
    {
        if (_grid.SelectedItem is DesignRow row) _rows.Remove(row);
    }

    private void Revert()
    {
        // Read again from the database, not from what was loaded: Revert is also what the user is told to press when the table changed.
        if (_generator is not null) _ = LoadAsync();

        _notice.IsVisible = false;
    }

    private void BuildColumns()
    {
        if (_grid.Columns.Count > 0) return;

        var types = _generator!.CommonTypes;

        _grid.Columns.Add(Template(StringKeys.TableDesignerColumnName, 160, () => Text(nameof(DesignRow.Name))));
        _grid.Columns.Add(Template(StringKeys.TableDesignerColumnType, 170, () => TypeBox(types)));
        _grid.Columns.Add(Template(StringKeys.TableDesignerColumnNullable, 90, () => Check(nameof(DesignRow.IsNullable))));
        _grid.Columns.Add(Template(StringKeys.TableDesignerColumnKey, 100, () => Check(nameof(DesignRow.IsPrimaryKey))));
        _grid.Columns.Add(Template(StringKeys.TableDesignerColumnDefault, 160, () => Text(nameof(DesignRow.DefaultValue))));
        _grid.Columns.Add(Template(StringKeys.TableDesignerColumnIdentity, 80, () => Check(nameof(DesignRow.IsIdentity))));
    }

    /// <summary>A column whose cells are always live controls, so there is no separate edit mode to enter.</summary>
    private static DataGridTemplateColumn Template(string headerKey, double width, Func<Control> cell)
    {
        var header = Localizer.Get(headerKey);

        // The width the header's own text needs, whatever the language: "Allow nulls" and "Primary key" were
        // cut to "Allow nu" and "Primary ke" at the widths picked for the cells.
        var needed = HeaderWidth(header);

        return new DataGridTemplateColumn
        {
            Header = header,
            Width = new DataGridLength(Math.Max(width, needed)),
            MinWidth = needed,
            CanUserResize = true,
            CellTemplate = new FuncDataTemplate<DesignRow>((_, _) => cell(), supportsRecycling: false),
        };
    }

    /// <summary>
    /// Room for a header: its text at the grid's size, and what Fluent's header takes besides, which is its
    /// padding on both sides, the space kept for a sort arrow, and the grip that resizes the column.
    /// </summary>
    internal static double HeaderWidth(string header)
    {
        var text = new TextBlock { Text = header, FontSize = PanelMetrics.FontSize };

        text.Measure(Size.Infinity);

        return Math.Ceiling(text.DesiredSize.Width) + HeaderChrome;
    }

    /// <summary>Padding (12 + 12), the sort arrow's room and the resize grip, measured on the Fluent header.</summary>
    private const double HeaderChrome = 52;

    private static TextBox Text(string property) => new()
    {
        FontSize = PanelMetrics.FontSize,
        BorderThickness = default,
        Background = Brushes.Transparent,
        VerticalContentAlignment = VerticalAlignment.Center,
        [!TextBox.TextProperty] = new Binding(property) { Mode = BindingMode.TwoWay },
    };

    private static ComboBox TypeBox(IReadOnlyList<string> types)
    {
        var box = NewTypeBox(types);

        // An editable ComboBox's text lives in a TextBox of the template, which keeps its own top alignment
        // whatever the ComboBox says: the text sat at the top of the row, above the name and the checkboxes.
        box.Styles.Add(new Style(x => x.OfType<ComboBox>().Template().OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
            },
        });

        return box;
    }

    private static ComboBox NewTypeBox(IReadOnlyList<string> types) => new()
    {
        FontSize = PanelMetrics.FontSize,
        IsEditable = true,
        ItemsSource = types,
        HorizontalAlignment = HorizontalAlignment.Stretch,

        // The text is in the row's line with the name and the checkboxes, not at the top of the cell.
        VerticalAlignment = VerticalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        BorderThickness = default,
        Background = Brushes.Transparent,
        [!ComboBox.TextProperty] = new Binding(nameof(DesignRow.DataType)) { Mode = BindingMode.TwoWay },
    };

    private static CheckBox Check(string property) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        [!Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty] = new Binding(property) { Mode = BindingMode.TwoWay },
    };

    private static Button Button(IconKind icon, string key, Action click)
    {
        var button = DatabaseButtons.Create(icon, Localizer.Get(key));

        button.Click += (_, _) => click();

        return button;
    }

    private void Place(Control control, int row)
    {
        SetRow(control, row);
        Children.Add(control);
    }
}

/// <summary>The window that shows a generated script and asks what to do with it.</summary>
internal static class TableScriptDialog
{
    public static async Task<ScriptChoice> AskAsync(Window owner, string title, TableScript script)
    {
        var choice = ScriptChoice.Cancel;

        var run = new Button { Content = Localizer.Get(StringKeys.TableDesignerApply), Name = "TableScriptRun", Classes = { "primary" }, IsDefault = !script.IsDestructive };
        var only = new Button { Content = Localizer.Get(StringKeys.TableDesignerScriptOnly), Name = "TableScriptOnly" };
        var cancel = new Button { Content = Localizer.Get(StringKeys.ButtonCancel), Name = "TableScriptCancel", IsCancel = true, IsDefault = script.IsDestructive };

        var dialog = new Window
        {
            Title = Localizer.Get(StringKeys.TableDesignerReviewTitle) + " - " + title,
            Width = 680,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        run.Click += (_, _) => { choice = ScriptChoice.Run; dialog.Close(); };
        only.Click += (_, _) => { choice = ScriptChoice.ScriptOnly; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        var notes = script.Warnings.ToList();

        if (script.Transaction is null)
            notes.Add(Localizer.Get(StringKeys.TableDesignerNoTransaction));

        var panel = new DockPanel { Margin = Spacing.DialogPadding, LastChildFill = true };

        var buttons = DialogButtons.Row(run, only, cancel);
        buttons.Margin = new Thickness(0, Spacing.Normal, 0, 0);
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        panel.Children.Add(buttons);

        if (notes.Count > 0)
        {
            var warning = new TextBlock
            {
                Text = string.Join("\n", notes),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, Spacing.Normal, 0, 0),
            }.Themed(TextBlock.ForegroundProperty, "WarningForegroundBrush");

            DockPanel.SetDock(warning, Avalonia.Controls.Dock.Bottom);
            panel.Children.Add(warning);
        }

        panel.Children.Add(new TextBox
        {
            Name = "TableScriptText",
            Text = script.Text,
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Menlo, Consolas, monospace"),
            FontSize = PanelMetrics.FontSize,
        });

        dialog.Content = panel;

        await dialog.ShowDialog(owner).ConfigureAwait(true);

        return choice;
    }
}
