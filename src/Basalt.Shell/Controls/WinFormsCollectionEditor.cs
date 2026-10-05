using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

/// <summary>A modal editor whose validated draft becomes one designer history entry.</summary>
internal sealed class WinFormsCollectionEditor : Window
{
    private readonly ListBox _list = new() { Name = "CollectionEntries", MinWidth = 150 };
    private readonly TextBox _text = new() { Name = "CollectionText" };
    private readonly TextBox _name = new() { Name = "CollectionName" };
    private readonly TextBox _width = new() { Name = "CollectionWidth" };
    private readonly ComboBox _type = new()
    {
        Name = "CollectionType",
        ItemsSource = WinFormsCollectionDocument.ColumnTypes,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly TextBlock _error = new()
    {
        Name = "CollectionError",
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
    };
    private readonly Button _save;
    private readonly Button _remove;
    private readonly Button _up;
    private readonly Button _down;
    private readonly List<string> _items = [];
    private readonly List<WinFormsColumnDraft> _columns = [];
    private readonly bool _isColumns;
    private bool _updating;

    public IReadOnlyList<string> Items => _items.ToArray();
    public IReadOnlyList<WinFormsColumnDefinition> Columns => _columns.Select(column =>
    {
        if (!column.TryCreateDefinition(out var definition))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsCollectionInvalidColumn));
        return definition!;
    }).ToArray();

    public WinFormsCollectionEditor(IReadOnlyList<string> items) : this(false)
    {
        _items.AddRange(items);
        Refresh(0);
    }

    public WinFormsCollectionEditor(IReadOnlyList<WinFormsColumnDefinition> columns) : this(true)
    {
        _columns.AddRange(columns.Select(column => new WinFormsColumnDraft(column)));
        Refresh(0);
    }

    private WinFormsCollectionEditor(bool columns)
    {
        _isColumns = columns;
        Title = Localizer.Get(columns ? "WinFormsCollection_ColumnsTitle" : "WinFormsCollection_ItemsTitle");
        Width = 630;
        Height = 430;
        MinWidth = 500;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _save = MakeButton("CollectionSave", "WinFormsCollection_Save", Save);
        _save.IsDefault = true;
        var cancel = MakeButton("CollectionCancel", "WinFormsCollection_Cancel", () => Close(false));
        cancel.IsCancel = true;
        _remove = MakeButton("CollectionRemove", "WinFormsCollection_Remove", Remove);
        _up = MakeButton("CollectionUp", "WinFormsCollection_Up", () => Move(-1));
        _down = MakeButton("CollectionDown", "WinFormsCollection_Down", () => Move(1));
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                MakeButton("CollectionAdd", "WinFormsCollection_Add", Add),
                _remove,
                _up,
                _down,
            },
        };
        foreach (var action in actions.Children)
            action.Margin = new Thickness(0, 8, 6, 0);

        var fields = BuildFields(columns);
        var list = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { _list, actions },
        };
        Grid.SetRow(actions, 1);
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,3*"),
            Children = { list, new ScrollViewer { Content = fields } },
        };
        Grid.SetColumn(body.Children[1], 1);
        Content = WinFormsEditorDialogLayout.CreateWithFixedBody(body, _save, cancel);

        _list.SelectionChanged += (_, _) => ShowSelected();
        _list.KeyDown += OnListKeyDown;
        foreach (var editor in new[] { _text, _name, _width })
        {
            // TextChanged is deferred; immediate notification also validates
            // a value typed immediately before a Save or navigation click.
            editor.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.TextProperty)
                    UpdateSelected();
            };
        }
        _type.SelectionChanged += (_, _) => UpdateSelected();
        Opened += (_, _) => _list.Focus();
    }

    private Control BuildFields(bool columns)
    {
        var fields = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12, 0, 0, 0),
        };
        if (columns)
        {
            fields.Children.Add(Field("WinFormsCollection_Name", _name));
            fields.Children.Add(Field("WinFormsCollection_Type", _type));
        }
        fields.Children.Add(Field(columns ? "WinFormsCollection_HeaderText" : "WinFormsCollection_Text", _text));
        if (columns)
            fields.Children.Add(Field("WinFormsCollection_Width", _width));
        fields.Children.Add(_error);
        return fields;
    }

    private static Button MakeButton(string name, string key, Action action)
    {
        var button = new Button { Name = name, Content = Localizer.Get(key) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Control Field(string key, Control editor) => new StackPanel
    {
        Spacing = Spacing.Tight,
        Children = { new TextBlock { Text = Localizer.Get(key), TextWrapping = Avalonia.Media.TextWrapping.Wrap }, editor },
    };

    private int Count => _isColumns ? _columns.Count : _items.Count;

    private void Refresh(int selection)
    {
        UpdateLabels(Count == 0 ? -1 : Math.Clamp(selection, 0, Count - 1));
        ShowSelected();
    }

    private void ShowSelected()
    {
        if (_updating)
            return;
        _updating = true;
        var index = _list.SelectedIndex;
        var selected = index >= 0;
        _text.IsEnabled = selected;
        _name.IsEnabled = selected;
        _type.IsEnabled = selected;
        _width.IsEnabled = selected;
        _text.Text = !selected ? "" : _isColumns ? _columns[index].HeaderText : _items[index];
        if (_isColumns)
        {
            _name.Text = selected ? _columns[index].Name : "";
            _type.SelectedItem = selected ? _columns[index].TypeName : null;
            _width.Text = selected ? _columns[index].Width : "";
        }
        _updating = false;
        ValidateDraft();
    }

    private void UpdateSelected()
    {
        if (_updating || _list.SelectedIndex < 0)
            return;
        var index = _list.SelectedIndex;
        if (_isColumns)
        {
            var column = _columns[index];
            column.Name = _name.Text ?? "";
            column.TypeName = _type.SelectedItem as string;
            column.HeaderText = _text.Text ?? "";
            column.Width = _width.Text ?? "";
        }
        else
        {
            _items[index] = _text.Text ?? "";
        }
        UpdateLabels(index);
        ValidateDraft();
    }

    private void ValidateDraft()
    {
        var invalid = -1;
        var reason = "WinFormsCollection_InvalidText";
        if (_isColumns)
        {
            reason = "WinFormsCollection_InvalidColumn";
            invalid = _columns.FindIndex(column => !column.TryCreateDefinition(out _)
                || _columns.Count(other => other.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase)) > 1);
        }
        else
        {
            invalid = _items.FindIndex(item => !WinFormsEditorText.IsSingleLine(item));
        }
        _save.IsEnabled = invalid < 0;
        _error.Text = invalid < 0 ? "" : Localizer.Get(StringKeys.WinFormsCollectionInvalidEntry, invalid + 1, Localizer.Get(reason));
        var selected = _list.SelectedIndex;
        _remove.IsEnabled = selected >= 0;
        _up.IsEnabled = selected > 0;
        _down.IsEnabled = selected >= 0 && selected < Count - 1;
    }

    private void UpdateLabels(int index)
    {
        _updating = true;
        _list.ItemsSource = _isColumns
            ? _columns.Select(column => column.Name + " — " + column.HeaderText).ToArray()
            : _items.ToArray();
        _list.SelectedIndex = index;
        _updating = false;
    }

    private void Save()
    {
        UpdateSelected();
        ValidateDraft();
        if (_save.IsEnabled)
            Close(true);
    }

    private void Add()
    {
        if (_isColumns)
        {
            var number = 1;
            while (_columns.Any(column => column.Name.Equals("Column" + number, StringComparison.OrdinalIgnoreCase)))
                number++;
            _columns.Add(new WinFormsColumnDraft(new WinFormsColumnDefinition(
                "Column" + number, "DataGridViewTextBoxColumn", "Column" + number)));
        }
        else
        {
            _items.Add(Localizer.Get(StringKeys.WinFormsCollectionNewItem));
        }
        Refresh(Count - 1);
    }

    private void Remove()
    {
        var index = _list.SelectedIndex;
        if (index < 0)
            return;
        if (_isColumns)
            _columns.RemoveAt(index);
        else
            _items.RemoveAt(index);
        Refresh(index);
    }

    private void Move(int direction)
    {
        var index = _list.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= Count)
            return;
        if (_isColumns)
            (_columns[index], _columns[target]) = (_columns[target], _columns[index]);
        else
            (_items[index], _items[target]) = (_items[target], _items[index]);
        Refresh(target);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Delete)
        {
            Remove();
            args.Handled = true;
        }
        else if (args.KeyModifiers.HasFlag(KeyModifiers.Alt) && args.Key is Key.Up or Key.Down)
        {
            Move(args.Key == Key.Up ? -1 : 1);
            args.Handled = true;
        }
    }
}
