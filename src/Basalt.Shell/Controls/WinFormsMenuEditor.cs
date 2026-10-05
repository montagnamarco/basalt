using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed class WinFormsMenuEditor : Window
{
    private readonly WinFormsMenuDocument _document;
    private readonly string _strip;
    private readonly List<WinFormsMenuItem> _items;
    private readonly ListBox _list = new() { Name = "MenuEntries" };
    private readonly TextBox _text = new() { Name = "MenuText" };
    private readonly ComboBox _parent = new() { Name = "MenuParent" };
    private readonly TextBlock _error = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private bool _refreshing;

    public IReadOnlyList<WinFormsMenuItem> Items => _items.ToArray();

    public WinFormsMenuEditor(WinFormsMenuDocument document, string strip)
    {
        _document = document;
        _strip = strip;
        _items = document.Read(strip).ToList();
        Title = Localizer.Get(StringKeys.MenuEditorTitle);
        Width = 640;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        toolbar.Children.Add(ActionButton("MenuEditor_Add", () => Add(false)));
        toolbar.Children.Add(ActionButton("MenuEditor_Separator", () => Add(true)));
        toolbar.Children.Add(ActionButton("Designer_Delete", Delete));
        toolbar.Children.Add(ActionButton("MenuEditor_Up", () => Move(-1)));
        toolbar.Children.Add(ActionButton("MenuEditor_Down", () => Move(1)));
        var detail = new StackPanel { Margin = new Thickness(8), Spacing = 8 };
        detail.Children.Add(new TextBlock { Text = Localizer.Get(StringKeys.ResourceEditorValue) });
        detail.Children.Add(_text);
        detail.Children.Add(new TextBlock { Text = Localizer.Get(StringKeys.MenuEditorParent) });
        detail.Children.Add(_parent);
        detail.Children.Add(_error);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = Spacing.PanelPadding };
        Grid.SetColumnSpan(toolbar, 2);
        body.Children.Add(toolbar);
        Grid.SetRow(_list, 1);
        body.Children.Add(_list);
        Grid.SetRow(detail, 1);
        Grid.SetColumn(detail, 1);
        body.Children.Add(detail);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var save = ActionButton("WinFormsCollection_Save", Save);
        save.IsDefault = true;
        actions.Children.Add(save);
        var cancel = ActionButton("WinFormsCollection_Cancel", () => Close(false));
        cancel.IsCancel = true;
        actions.Children.Add(cancel);
        Grid.SetRow(actions, 2);
        Grid.SetColumnSpan(actions, 2);
        body.Children.Add(actions);
        Content = body;
        _list.SelectionChanged += (_, _) => ShowSelection();
        _text.TextChanged += (_, _) => Change();
        _parent.SelectionChanged += (_, _) => Change();
        Refresh(0);
    }

    private static Button ActionButton(string key, Action action)
    {
        var button = new Button { Content = Localizer.Get(key) };
        button.Click += (_, _) => action();
        return button;
    }

    private void Refresh(int selected)
    {
        _refreshing = true;
        try
        {
            _list.ItemsSource = _items.Select(item => new TextBlock { Text = DisplayItem(item), FontSize = 12 }).ToArray();
            _list.SelectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selected, 0, _items.Count - 1);
        }
        finally
        {
            _refreshing = false;
        }
        ShowSelection();
    }

    private void ShowSelection()
    {
        if (_refreshing)
        {
            return;
        }
        _refreshing = true;
        try
        {
            var index = _list.SelectedIndex;
            var item = index >= 0 ? _items[index] : null;
            _text.Text = item?.Text ?? "";
            _text.IsEnabled = item is { IsSeparator: false };
            _parent.ItemsSource = new[] { _strip }.Concat(_items.Where(candidate => !candidate.IsSeparator && candidate != item).Select(candidate => candidate.Name)).ToArray();
            _parent.SelectedItem = item?.Parent;
            _parent.IsEnabled = item is not null;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Change()
    {
        if (_refreshing || _list.SelectedIndex < 0)
        {
            return;
        }
        var index = _list.SelectedIndex;
        _items[index] = _items[index] with { Text = _text.Text ?? "", Parent = _parent.SelectedItem as string ?? _strip };
        if (_list.SelectedItem is TextBlock label) label.Text = DisplayItem(_items[index]);
    }

    private string DisplayItem(WinFormsMenuItem item)
    {
        var depth = 0;
        var parent = item.Parent;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!parent.Equals(_strip, StringComparison.OrdinalIgnoreCase) && visited.Add(parent))
        {
            var ancestor = _items.FirstOrDefault(candidate => candidate.Name.Equals(parent, StringComparison.OrdinalIgnoreCase));
            if (ancestor is null)
            {
                break;
            }
            depth++;
            parent = ancestor.Parent;
        }
        return new string(' ', depth * 4) + (item.IsSeparator ? "—" : item.Text) + "  [" + item.Name + "]";
    }

    private void Add(bool separator)
    {
        var prefix = separator ? "ToolStripSeparator" : "ToolStripMenuItem";
        var number = 1;
        var declared = _document.DeclaredNames.Concat(_items.Select(item => item.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (declared.Contains(prefix + number))
        {
            number++;
        }
        _items.Add(new(prefix + number, _strip, separator ? "" : Localizer.Get(StringKeys.MenuEditorNewItem), separator));
        Refresh(_items.Count - 1);
    }

    private void Delete()
    {
        if (_list.SelectedIndex < 0)
        {
            return;
        }
        var index = _list.SelectedIndex;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _items[index].Name };
        while (true)
        {
            var descendants = _items.Where(item => names.Contains(item.Parent)).Select(item => item.Name).ToArray();
            var count = names.Count;
            names.UnionWith(descendants);
            if (count == names.Count)
            {
                break;
            }
        }
        _items.RemoveAll(item => names.Contains(item.Name));
        Refresh(index);
    }

    private void Move(int delta)
    {
        var index = _list.SelectedIndex;
        if (index < 0 || index + delta < 0 || index + delta >= _items.Count)
        {
            return;
        }
        (_items[index], _items[index + delta]) = (_items[index + delta], _items[index]);
        Refresh(index + delta);
    }

    private void Save()
    {
        try
        {
            _document.Set(_strip, _items);
            Close(true);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            _error.Text = Localizer.Get(exception is NotSupportedException
                ? "WinFormsCollection_Unsupported"
                : "MenuEditor_InvalidMenu");
        }
    }
}
