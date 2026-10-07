using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal enum WinFormsMenuDesignAction
{
    AddItem,
    AddSeparator,
    ChangeText,
    DeleteItem,
}

internal sealed record WinFormsMenuDesignChange(
    WinFormsMenuDesignAction Action,
    string Parent,
    string? Name = null,
    string? Text = null);

/// <summary>Design-time menu fields backed only by literal VB menu declarations.</summary>
internal sealed class WinFormsMenuDesignEditor : Canvas
{
    private readonly Border _bar = new() { Name = "MenuInlineBar" };
    private readonly Border _dropdown = new() { Name = "MenuInlineDropdown" };
    private IReadOnlyList<WinFormsMenuItem> _items = [];
    private string _strip = "";
    private string? _openParent;
    private string? _selectedItemName;
    private bool _unsupported;

    internal event Action<WinFormsMenuDesignChange>? EditRequested;

    internal event Action<string?>? SelectedItemChanged;

    internal event Action<string>? DefaultEventRequested;

    internal string? SelectedItemName => _selectedItemName;

    internal WinFormsMenuDesignEditor()
    {
        _bar.Background = Brushes.WhiteSmoke;
        _bar.BorderBrush = Brushes.DodgerBlue;
        _bar.BorderThickness = new Thickness(1);
        _dropdown.Background = Brushes.White;
        _dropdown.BorderBrush = Brushes.DodgerBlue;
        _dropdown.BorderThickness = new Thickness(1);
        _dropdown.IsVisible = false;
        Children.Add(_bar);
        Children.Add(_dropdown);
    }

    internal void Show(string strip, IReadOnlyList<WinFormsMenuItem> items, double menuWidth,
        bool unsupported = false)
    {
        if (!string.Equals(_strip, strip, StringComparison.OrdinalIgnoreCase))
        {
            _openParent = null;
            _selectedItemName = null;
        }

        _strip = strip;
        _items = items;
        _unsupported = unsupported;

        if (_openParent is not null && !_items.Any(item => item.Name == _openParent))
        {
            _openParent = null;
        }

        if (_selectedItemName is not null && !_items.Any(item => item.Name == _selectedItemName))
        {
            _selectedItemName = null;
        }

        _bar.Width = Math.Max(90, menuWidth - 30);
        Width = Math.Max(250, menuWidth);
        Rebuild();
        SelectedItemChanged?.Invoke(_selectedItemName);
    }

    internal void SelectPendingItem(string name)
    {
        SetSelectedItem(name);
    }

    private void Rebuild()
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(2, 1),
        };

        foreach (var item in ChildrenOf(_strip))
        {
            var button = MenuButton(item);
            button.Click += (_, _) =>
            {
                SetSelectedItem(item.Name);
                if (!item.IsSeparator)
                {
                    _openParent = item.Name;
                }
                Rebuild();
            };
            bar.Children.Add(button);
        }

        if (!_unsupported)
        {
            bar.Children.Add(Placeholder("MenuInlineTopPlaceholder", _strip));
        }

        _bar.Child = bar;
        Canvas.SetTop(_dropdown, 25);
        Canvas.SetLeft(_dropdown, 4);
        BuildDropdown();
        Height = _dropdown.IsVisible ? 310 : 28;
    }

    private void BuildDropdown()
    {
        if (_unsupported)
        {
            _dropdown.IsVisible = true;
            _dropdown.Child = new TextBlock
            {
                Text = Localizer.Get(StringKeys.MenuInlineUnsupported),
                Margin = new Thickness(8),
                TextWrapping = TextWrapping.Wrap,
                Width = 230,
            };
            return;
        }

        if (_openParent is null)
        {
            _dropdown.IsVisible = false;
            _dropdown.Child = null;
            return;
        }

        var rows = new StackPanel { Spacing = Spacing.Tight, Margin = new Thickness(6), Width = 230 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var back = new Button
        {
            Name = "MenuInlineBack",
            Content = Localizer.Get(StringKeys.MenuInlineBack),
        };
        back.Click += (_, _) =>
        {
            var parent = WinFormsMenuTreeHelpers.Back(_items, _openParent, _strip);
            _openParent = parent.Equals(_strip, StringComparison.OrdinalIgnoreCase) ? null : parent;
            Rebuild();
        };
        header.Children.Add(back);
        header.Children.Add(new TextBlock
        {
            Text = OpenPath(),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = 165,
        });
        rows.Children.Add(header);

        foreach (var item in ChildrenOf(_openParent))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Spacing.Tight };
            var button = MenuButton(item);
            button.Width = 195;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) =>
            {
                SetSelectedItem(item.Name);
                Rebuild();
            };
            row.Children.Add(button);

            if (!item.IsSeparator)
            {
                var open = new Button
                {
                    Name = "MenuInlineOpen_" + item.Name,
                    Content = "›",
                    Width = 25,
                };
                ToolTip.SetTip(open, Localizer.Get(StringKeys.MenuInlineOpenSubmenu));
                open.Click += (_, _) =>
                {
                    SetSelectedItem(item.Name);
                    _openParent = item.Name;
                    Rebuild();
                };
                row.Children.Add(open);
            }

            rows.Children.Add(row);
        }

        rows.Children.Add(Placeholder("MenuInlineChildPlaceholder", _openParent));
        var separator = new Button
        {
            Name = "MenuInlineSeparator",
            Content = Localizer.Get(StringKeys.MenuEditorSeparator),
        };
        separator.Click += (_, _) => EditRequested?.Invoke(new(
            WinFormsMenuDesignAction.AddSeparator, _openParent));
        rows.Children.Add(separator);
        WinFormsMenuTreeHelpers.AddCaptionEditor(rows, _items, _selectedItemName,
            selected => !selected.IsSeparator, "MenuInline",
            (selected, text) => EditRequested?.Invoke(new(WinFormsMenuDesignAction.ChangeText,
                selected.Parent, selected.Name, text)));

        _dropdown.Child = new ScrollViewer
        {
            Content = rows,
            MaxHeight = 280,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _dropdown.IsVisible = true;
    }

    private TextBox Placeholder(string name, string parent) =>
        WinFormsMenuTreeHelpers.TypeHereInput(name, text =>
            EditRequested?.Invoke(new(WinFormsMenuDesignAction.AddItem, parent, Text: text)));

    private Button MenuButton(WinFormsMenuItem item)
    {
        var button = new Button
        {
            Name = "MenuInlineItem_" + item.Name,
            Content = item.IsSeparator ? "────────" : WinFormsMenuTreeHelpers.DisplayText(item.Text),
            Background = item.Name == _selectedItemName ? Brushes.LightBlue : Brushes.Transparent,
        };
        if (!item.IsSeparator)
        {
            button.DoubleTapped += (_, args) =>
            {
                SetSelectedItem(item.Name);
                DefaultEventRequested?.Invoke(item.Name);
                args.Handled = true;
            };
        }
        var delete = new MenuItem { Name = "MenuInlineDelete_" + item.Name, Header = Localizer.Get(StringKeys.DesignerDelete) };
        delete.Click += (_, _) => RequestDelete(item);
        button.ContextMenu = new ContextMenu { ItemsSource = new object[] { delete } };
        return button;
    }

    /// <summary>Deletes item and its whole subtree: Visual Studio's Delete key with the
    /// selected item's own tree, and the "Delete" entry its context menu offers for the same
    /// action without touching the keyboard.</summary>
    private void RequestDelete(WinFormsMenuItem item)
    {
        SetSelectedItem(item.Name);
        EditRequested?.Invoke(new(WinFormsMenuDesignAction.DeleteItem, item.Parent, item.Name));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // A TextBox (the caption editor, "Type Here") consumes its own Backspace/Delete while
        // editing text; this check is only a backstop against a focus corner case, not the
        // primary guard, so Backspace inside one of those never deletes the selected item.
        if (e.Handled || e.Source is TextBox || e.Key is not (Key.Delete or Key.Back)
            || _selectedItemName is not { } name) return;
        if (_items.FirstOrDefault(item => item.Name == name) is not { } selected) return;
        RequestDelete(selected);
        e.Handled = true;
    }

    private IEnumerable<WinFormsMenuItem> ChildrenOf(string parent) =>
        _items.Where(item => item.Parent.Equals(parent, StringComparison.OrdinalIgnoreCase));

    private string OpenPath() => WinFormsMenuTreeHelpers.OpenPath(_items, _openParent, _strip);

    private void SetSelectedItem(string? name)
    {
        if (_selectedItemName == name)
        {
            return;
        }
        _selectedItemName = name;
        SelectedItemChanged?.Invoke(name);
    }

}
