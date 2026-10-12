using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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

    /// <summary>Gives keyboard focus back to the rebuilt button of the selected item. A click here
    /// rebuilds the rows, which replaces the button the press had focused; Avalonia drops focus with
    /// that button, so Delete (see <see cref="OnKeyDown"/>) reached nothing after a plain click. The
    /// rows sit in a ScrollViewer whose content joins the visual tree only once its template is
    /// applied, hence the layout pass. Called only from the panel's own clicks, never from Show().</summary>
    private void FocusSelectedItemButton()
    {
        if (_selectedItemName is null) return;
        UpdateLayout();
        var name = "ToolStripDropDownItem_" + _selectedItemName;
        this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == name)?.Focus();
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
                _openParent = WinFormsMenuTreeHelpers.Back(_items, _openParent, _owner);
                Rebuild();
                FocusSelectedItemButton();
            };
            header.Children.Add(back);
            header.Children.Add(new TextBlock
            {
                Text = WinFormsMenuTreeHelpers.OpenPath(_items, _openParent, _owner),
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
                Content = item.IsSeparator ? "────────" : WinFormsMenuTreeHelpers.DisplayText(item.Text),
                Background = item.Name == _selectedItemName ? Brushes.LightBlue : Brushes.Transparent,
                Width = canNest ? 183 : 208,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) =>
            {
                SetSelectedItem(item.Name);
                Rebuild();
                FocusSelectedItemButton();
            };
            var delete = new MenuItem { Name = "ToolStripDropDownDelete_" + item.Name, Header = Localizer.Get(StringKeys.DesignerDelete) };
            delete.Click += (_, _) => RequestDelete(item);
            button.ContextMenu = new ContextMenu { ItemsSource = new object[] { delete } };
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
                    FocusSelectedItemButton();
                };
                row.Children.Add(open);
            }

            rows.Children.Add(row);
        }

        rows.Children.Add(TypeHereRow());
        WinFormsMenuTreeHelpers.AddCaptionEditor(rows, _items, _selectedItemName,
            selected => !selected.IsSeparator && selected.TypeName.Equals("ToolStripMenuItem", StringComparison.OrdinalIgnoreCase),
            "ToolStripDropDown",
            (selected, text) => EditRequested?.Invoke(new(WinFormsMenuDesignAction.ChangeText, selected.Parent, selected.Name, text)));

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
        var parent = _openParent;
        var input = WinFormsMenuTreeHelpers.TypeHereInput("ToolStripDropDownTypeHere", text =>
            EditRequested?.Invoke(new(WinFormsMenuDesignAction.AddItem, parent, Text: text, TypeName: "ToolStripMenuItem")));
        input.Width = 150;
        row.Children.Add(input);

        var arrow = new ToggleButton { Name = "ToolStripDropDownTypeArrow", Content = "▾", IsChecked = _typePickerOpen };
        ToolTip.SetTip(arrow, Localizer.Get(StringKeys.WinFormsToolStripChooseType));
        arrow.Click += (_, _) =>
        {
            _typePickerOpen = !_typePickerOpen;
            Rebuild();
            FocusSelectedItemButton();
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

    /// <summary>Deletes item and its own nested subtree (if it owns one): Visual Studio's Delete
    /// key with the selected item's own tree, and the "Delete" entry its context menu offers for
    /// the same action without touching the keyboard, mirroring <see
    /// cref="WinFormsMenuDesignEditor.RequestDelete"/> for this panel's own cascading tree.</summary>
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
        // primary guard, mirroring WinFormsMenuDesignEditor's own comment on the same pattern.
        if (e.Handled || e.Source is TextBox || e.Key is not (Key.Delete or Key.Back)
            || _selectedItemName is not { } name) return;
        if (_items.FirstOrDefault(item => item.Name == name) is not { } selected) return;
        RequestDelete(selected);
        e.Handled = true;
    }

    private static string DisplayKey(string typeName) => typeName switch
    {
        "ToolStripMenuItem" => StringKeys.WinFormsToolStripMenuItem,
        "ToolStripComboBox" => StringKeys.WinFormsToolStripComboBox,
        "ToolStripSeparator" => StringKeys.WinFormsToolStripSeparator,
        "ToolStripTextBox" => StringKeys.WinFormsToolStripTextBox,
        _ => StringKeys.WinFormsToolStripMenuItem,
    };
}
