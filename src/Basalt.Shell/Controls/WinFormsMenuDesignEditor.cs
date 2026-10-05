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
            _openParent = _items.FirstOrDefault(item => item.Name == _openParent)?.Parent;
            if (_openParent == _strip)
            {
                _openParent = null;
            }
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
        AddCaptionEditor(rows);

        _dropdown.Child = new ScrollViewer
        {
            Content = rows,
            MaxHeight = 280,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _dropdown.IsVisible = true;
    }

    private void AddCaptionEditor(StackPanel rows)
    {
        var selected = _items.FirstOrDefault(item => item.Name == _selectedItemName);
        if (selected is null || selected.IsSeparator)
        {
            return;
        }

        rows.Children.Add(new TextBlock { Text = Localizer.Get(StringKeys.MenuInlineEditText) });
        var caption = new TextBox
        {
            Name = "MenuInlineCaption",
            Text = selected.Text,
        };
        void CommitCaption()
        {
            if (caption.Text is { } text && text != selected.Text)
            {
                EditRequested?.Invoke(new(WinFormsMenuDesignAction.ChangeText,
                    selected.Parent, selected.Name, text));
            }
        }

        caption.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter)
            {
                return;
            }
            CommitCaption();
            args.Handled = true;
        };
        rows.Children.Add(caption);

        var apply = new Button
        {
            Name = "MenuInlineApplyCaption",
            Content = Localizer.Get(StringKeys.ResourceEditorApply),
        };
        apply.Click += (_, _) => CommitCaption();
        rows.Children.Add(apply);
    }

    private TextBox Placeholder(string name, string parent)
    {
        var input = new TextBox
        {
            Name = name,
            PlaceholderText = Localizer.Get(StringKeys.MenuInlineTypeHere),
            MinWidth = 90,
        };
        input.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text))
            {
                return;
            }

            var text = input.Text.Trim();
            input.Text = "";
            EditRequested?.Invoke(new(WinFormsMenuDesignAction.AddItem, parent, Text: text));
            args.Handled = true;
        };
        return input;
    }

    private Button MenuButton(WinFormsMenuItem item)
    {
        var button = new Button
        {
            Name = "MenuInlineItem_" + item.Name,
            Content = item.IsSeparator ? "────────" : DisplayText(item.Text),
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
        return button;
    }

    private IEnumerable<WinFormsMenuItem> ChildrenOf(string parent) =>
        _items.Where(item => item.Parent.Equals(parent, StringComparison.OrdinalIgnoreCase));

    private string OpenPath()
    {
        var path = new List<string>();
        var current = _openParent;
        while (current is not null && !current.Equals(_strip, StringComparison.OrdinalIgnoreCase))
        {
            var item = _items.FirstOrDefault(candidate => candidate.Name == current);
            if (item is null)
            {
                break;
            }
            path.Add(DisplayText(item.Text));
            current = item.Parent;
        }
        path.Reverse();
        return string.Join(" › ", path);
    }

    private void SetSelectedItem(string? name)
    {
        if (_selectedItemName == name)
        {
            return;
        }
        _selectedItemName = name;
        SelectedItemChanged?.Invoke(name);
    }

    private static string DisplayText(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '&' && index + 1 < source.Length && source[index + 1] == '&')
            {
                result.Append('&');
                index++;
            }
            else if (source[index] != '&')
            {
                result.Append(source[index]);
            }
        }
        return result.ToString();
    }
}
