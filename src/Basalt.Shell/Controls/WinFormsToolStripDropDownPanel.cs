using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed record WinFormsToolStripDropDownChange(
    WinFormsMenuDesignAction Action, string Parent, string? Name = null, string? Text = null,
    string TypeName = "ToolStripMenuItem");

/// <summary>
/// The cascading drop-down a ToolStripDropDownButton or ToolStripSplitButton owns, shown inside
/// <see cref="WinFormsToolStripDesignEditor"/> when one of those two items is selected: its own
/// DropDownItems, a "Type Here" row, and the arrow Visual Studio shows next to it offering
/// MenuItem, ComboBox, Separator and TextBox. Unlike the plain "Type Here" box a flat ToolStrip
/// Items row paints (see <see cref="WinFormsToolStripDesignEditor"/>), a nested ToolStripMenuItem
/// here can itself own further sub-items, the same cascading shape
/// <see cref="WinFormsMenuDesignEditor"/> already paints for a MenuStrip: this panel is its own
/// control, written fresh for the arrow of four kinds Visual Studio offers here, but both read
/// and write through <see cref="WinFormsMenuDocument"/> so there is still only one model for a
/// cascading item tree, whichever kind of item owns its root.
/// </summary>
internal sealed class WinFormsToolStripDropDownPanel : Canvas
{
    private static readonly (string Display, string TypeName)[] Kinds =
    [
        ("MenuItem", "ToolStripMenuItem"),
        ("ComboBox", "ToolStripComboBox"),
        ("Separator", "ToolStripSeparator"),
        ("TextBox", "ToolStripTextBox"),
    ];

    private IReadOnlyList<WinFormsMenuItem> _items = [];
    private string _owner = "";
    private string _openParent = "";
    private string? _selectedItemName;
    private bool _unsupported;
    private bool _typePickerOpen;

    internal event Action<WinFormsToolStripDropDownChange>? EditRequested;

    internal event Action<string?>? SelectedItemChanged;

    internal string? SelectedItemName => _selectedItemName;

    internal WinFormsToolStripDropDownPanel()
    {
        Width = 230;
    }

    internal void Show(string owner, IReadOnlyList<WinFormsMenuItem> items, bool unsupported = false)
    {
        if (!string.Equals(_owner, owner, StringComparison.OrdinalIgnoreCase))
        {
            _openParent = owner;
            _selectedItemName = null;
            _typePickerOpen = false;
        }

        _owner = owner;
        _items = items;
        _unsupported = unsupported;
        if (!_openParent.Equals(owner, StringComparison.OrdinalIgnoreCase) &&
            !_items.Any(item => item.Name.Equals(_openParent, StringComparison.OrdinalIgnoreCase)))
        {
            _openParent = owner;
        }

        if (_selectedItemName is not null && !_items.Any(item => item.Name == _selectedItemName))
        {
            _selectedItemName = null;
        }

        Rebuild();
        SelectedItemChanged?.Invoke(_selectedItemName);
    }

    internal void SelectPendingItem(string name) => SetSelectedItem(name);

    private void SetSelectedItem(string? name)
    {
        if (_selectedItemName == name)
        {
            return;
        }
        _selectedItemName = name;
        SelectedItemChanged?.Invoke(name);
    }

    private IEnumerable<WinFormsMenuItem> ChildrenOf(string parent) =>
        _items.Where(item => item.Parent.Equals(parent, StringComparison.OrdinalIgnoreCase));

    private void Rebuild()
    {
        Children.Clear();
        if (_unsupported)
        {
            Children.Add(new TextBlock
            {
                Name = "ToolStripDropDownUnsupported",
                Text = Localizer.Get(StringKeys.WinFormsToolStripUnsupported),
                Margin = new Thickness(8),
                TextWrapping = TextWrapping.Wrap,
                Width = 214,
            });
            Height = 60;
            return;
        }

        var rows = new StackPanel { Spacing = Spacing.Tight, Margin = new Thickness(6), Width = 218 };

        if (!_openParent.Equals(_owner, StringComparison.OrdinalIgnoreCase))
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var back = new Button { Name = "ToolStripDropDownBack", Content = Localizer.Get(StringKeys.MenuInlineBack) };
            back.Click += (_, _) =>
            {
                _openParent = _items.FirstOrDefault(item => item.Name == _openParent)?.Parent ?? _owner;
                Rebuild();
            };
            header.Children.Add(back);
            header.Children.Add(new TextBlock
            {
                Text = OpenPath(),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Width = 140,
            });
            rows.Children.Add(header);
        }

        foreach (var item in ChildrenOf(_openParent))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Spacing.Tight };
            var canNest = item.TypeName.Equals("ToolStripMenuItem", StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Name = "ToolStripDropDownItem_" + item.Name,
                Content = item.IsSeparator ? "────────" : DisplayText(item.Text),
                Background = item.Name == _selectedItemName ? Brushes.LightBlue : Brushes.Transparent,
                Width = canNest ? 183 : 208,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) =>
            {
                SetSelectedItem(item.Name);
                Rebuild();
            };
            row.Children.Add(button);

            if (canNest)
            {
                var open = new Button { Name = "ToolStripDropDownOpen_" + item.Name, Content = "›", Width = 25 };
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

        rows.Children.Add(TypeHereRow());
        AddCaptionEditor(rows);

        Children.Add(new ScrollViewer
        {
            Content = rows,
            MaxHeight = 280,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Width = 230,
        });
        Height = Math.Min(70 + ChildrenOf(_openParent).Count() * 32, 310);
    }

    private Control TypeHereRow()
    {
        var stack = new StackPanel { Spacing = Spacing.Tight };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Spacing.Tight };
        var input = new TextBox
        {
            Name = "ToolStripDropDownTypeHere",
            PlaceholderText = Localizer.Get(StringKeys.MenuInlineTypeHere),
            MinWidth = 90,
            Width = 150,
        };
        var parent = _openParent;
        input.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text))
            {
                return;
            }
            var text = input.Text.Trim();
            input.Text = "";
            EditRequested?.Invoke(new(WinFormsMenuDesignAction.AddItem, parent, Text: text, TypeName: "ToolStripMenuItem"));
            args.Handled = true;
        };
        row.Children.Add(input);

        var arrow = new ToggleButton { Name = "ToolStripDropDownTypeArrow", Content = "▾", IsChecked = _typePickerOpen };
        ToolTip.SetTip(arrow, Localizer.Get(StringKeys.WinFormsToolStripChooseType));
        arrow.Click += (_, _) =>
        {
            _typePickerOpen = !_typePickerOpen;
            Rebuild();
        };
        row.Children.Add(arrow);
        stack.Children.Add(row);

        if (_typePickerOpen)
        {
            foreach (var kind in Kinds)
            {
                var button = new Button
                {
                    Name = "ToolStripDropDownType_" + kind.TypeName,
                    Content = Localizer.Get(DisplayKey(kind.TypeName)),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Width = 206,
                };
                button.Click += (_, _) =>
                {
                    _typePickerOpen = false;
                    EditRequested?.Invoke(kind.TypeName.Equals("ToolStripSeparator", StringComparison.OrdinalIgnoreCase)
                        ? new(WinFormsMenuDesignAction.AddSeparator, parent)
                        : new(WinFormsMenuDesignAction.AddItem, parent, Text: "", TypeName: kind.TypeName));
                };
                stack.Children.Add(button);
            }
        }

        return stack;
    }

    private void AddCaptionEditor(StackPanel rows)
    {
        var selected = _items.FirstOrDefault(item => item.Name == _selectedItemName);
        if (selected is null || selected.IsSeparator ||
            !selected.TypeName.Equals("ToolStripMenuItem", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        rows.Children.Add(new TextBlock { Text = Localizer.Get(StringKeys.MenuInlineEditText) });
        var caption = new TextBox { Name = "ToolStripDropDownCaption", Text = selected.Text };
        void CommitCaption()
        {
            if (caption.Text is { } text && text != selected.Text)
            {
                EditRequested?.Invoke(new(WinFormsMenuDesignAction.ChangeText, selected.Parent, selected.Name, text));
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

        var apply = new Button { Name = "ToolStripDropDownApplyCaption", Content = Localizer.Get(StringKeys.ResourceEditorApply) };
        apply.Click += (_, _) => CommitCaption();
        rows.Children.Add(apply);
    }

    private string OpenPath()
    {
        var path = new List<string>();
        var current = _openParent;
        while (current is not null && !current.Equals(_owner, StringComparison.OrdinalIgnoreCase))
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

    private static string DisplayKey(string typeName) => typeName switch
    {
        "ToolStripMenuItem" => StringKeys.WinFormsToolStripMenuItem,
        "ToolStripComboBox" => StringKeys.WinFormsToolStripComboBox,
        "ToolStripSeparator" => StringKeys.WinFormsToolStripSeparator,
        "ToolStripTextBox" => StringKeys.WinFormsToolStripTextBox,
        _ => StringKeys.WinFormsToolStripMenuItem,
    };

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
