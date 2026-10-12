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

internal sealed record WinFormsToolStripDesignChange(string TypeName, string Text);

/// <summary>
/// The "Type Here" box a ToolStrip or StatusStrip shows at the end of its own Items, with the
/// drop-down arrow Visual Studio paints next to it to choose which <see cref="Forms.ToolStripItem"/>
/// subtype to add. It shares its hosting, positioning and hit-testing with
/// <see cref="WinFormsMenuDesignEditor"/> through <see cref="WinFormsDesignSurface"/>'s single
/// inline-overlay slot, but renders a flat row of items instead of a nested menu tree because a
/// ToolStrip's own Items collection has no submenu to walk.
/// </summary>
internal sealed class WinFormsToolStripDesignEditor : Canvas
{
    private readonly Border _bar = new() { Name = "ToolStripInlineBar" };
    private readonly Border _dropdown = new() { Name = "ToolStripInlineDropdown" };
    // A ToolStripDropDownButton/SplitButton item owns its own cascading DropDownItems, edited
    // through this single panel rather than a second copy of WinFormsMenuDesignEditor's tree
    // logic: see WinFormsToolStripDropDownPanel for why it is its own class. It is itself a bare
    // Canvas with no paint of its own, so it is hosted in this Border the same way _dropdown
    // hosts the flat type picker, giving it a paintable surface a test can rasterise.
    private readonly Border _dropDownHost = new() { Name = "ToolStripDropDownHost" };
    private readonly WinFormsToolStripDropDownPanel _dropDownPanel = new();
    private IReadOnlyList<WinFormsToolStripItem> _items = [];
    private IReadOnlyList<WinFormsMenuItem> _dropDownItems = [];
    private bool _dropDownUnsupported;
    private string _strip = "";
    private bool _statusStrip;
    private bool _typePickerOpen;
    private bool _unsupported;
    private string? _selectedItemName;

    internal event Action<WinFormsToolStripDesignChange>? EditRequested;

    internal event Action<string?>? SelectedItemChanged;

    /// <summary>Raised with the selected item's name by Delete/Backspace (not while typing in a
    /// TextBox) or by the "Delete" entry its context menu offers for the same action, mirroring
    /// <see cref="WinFormsMenuDesignEditor"/>'s own DeleteItem action for a flat row of items
    /// (there is no cascading tree here, so no second enum action is needed).</summary>
    internal event Action<string>? DeleteItemRequested;

    /// <summary>Raised when the nested drop-down panel for the selected DropDownButton/SplitButton
    /// item wants to add, remove or retext one of its own DropDownItems.</summary>
    internal event Action<WinFormsToolStripDropDownChange>? DropDownEditRequested;

    /// <summary>Raised when the selection inside the nested drop-down panel changes, carrying the
    /// name of the item selected there (or null), distinct from <see cref="SelectedItemChanged"/>
    /// which only ever reports a top-level flat Items selection.</summary>
    internal event Action<string?>? DropDownSelectedItemChanged;

    internal string? SelectedItemName => _selectedItemName;

    /// <summary>The top-level item the nested drop-down panel is currently open for, or null when
    /// the current selection does not own one.</summary>
    internal WinFormsToolStripItem? SelectedDropDownOwner => _selectedItemName is { } name
        ? _items.FirstOrDefault(item => item.Name == name && IsDropDownCapable(item.TypeName))
        : null;

    private static bool IsDropDownCapable(string typeName) =>
        typeName.Equals("ToolStripDropDownButton", StringComparison.OrdinalIgnoreCase) ||
        typeName.Equals("ToolStripSplitButton", StringComparison.OrdinalIgnoreCase);

    internal WinFormsToolStripDesignEditor()
    {
        _bar.Background = Brushes.WhiteSmoke;
        _bar.BorderBrush = Brushes.DodgerBlue;
        _bar.BorderThickness = new Thickness(1);
        _dropdown.Background = Brushes.White;
        _dropdown.BorderBrush = Brushes.DodgerBlue;
        _dropdown.BorderThickness = new Thickness(1);
        _dropdown.IsVisible = false;
        _dropDownHost.Background = Brushes.White;
        _dropDownHost.BorderBrush = Brushes.DodgerBlue;
        _dropDownHost.BorderThickness = new Thickness(1);
        _dropDownHost.IsVisible = false;
        _dropDownHost.Child = _dropDownPanel;
        Children.Add(_bar);
        Children.Add(_dropdown);
        Children.Add(_dropDownHost);
        _dropDownPanel.EditRequested += change => DropDownEditRequested?.Invoke(change);
        _dropDownPanel.SelectedItemChanged += name => DropDownSelectedItemChanged?.Invoke(name);
    }

    internal void Show(string strip, IReadOnlyList<WinFormsToolStripItem> items, bool statusStrip,
        double stripWidth, bool unsupported = false)
    {
        if (!string.Equals(_strip, strip, StringComparison.OrdinalIgnoreCase))
        {
            _typePickerOpen = false;
            _selectedItemName = null;
        }

        _strip = strip;
        _items = items;
        _statusStrip = statusStrip;
        _unsupported = unsupported;

        if (_selectedItemName is not null && !_items.Any(item => item.Name == _selectedItemName))
        {
            _selectedItemName = null;
        }

        _bar.Width = Math.Max(90, stripWidth - 30);
        Width = Math.Max(250, stripWidth);
        Rebuild();
        SelectedItemChanged?.Invoke(_selectedItemName);
    }

    /// <summary>Selects the item a pending add is about to create, before the document commits
    /// it, so the Properties panel already targets it once that commit's Show() call arrives.</summary>
    internal void SelectPendingItem(string name) => SetSelectedItem(name);

    /// <summary>Feeds the DropDownItems tree of the currently selected DropDownButton/SplitButton
    /// item (see <see cref="SelectedDropDownOwner"/>) to the nested panel; a no-op while the
    /// selection is not one of those two kinds.</summary>
    internal void ShowDropDownItems(IReadOnlyList<WinFormsMenuItem> items, bool unsupported = false)
    {
        _dropDownItems = items;
        _dropDownUnsupported = unsupported;
        if (SelectedDropDownOwner is not null)
        {
            Rebuild();
        }
    }

    /// <summary>Selects the nested item a pending add is about to create inside the currently
    /// open drop-down panel, mirroring <see cref="SelectPendingItem"/> for the top level.</summary>
    internal void SelectPendingDropDownItem(string name) => _dropDownPanel.SelectPendingItem(name);

    private void SetSelectedItem(string? name)
    {
        if (_selectedItemName == name)
        {
            return;
        }
        _selectedItemName = name;
        SelectedItemChanged?.Invoke(name);
    }

    /// <summary>Gives keyboard focus back to the rebuilt button of the selected item. A click
    /// rebuilds the bar, which replaces the very button the press had focused: Avalonia drops focus
    /// with that button, so Delete (see <see cref="OnKeyDown"/>) reached no editor after a plain click.
    /// Called only from the item's own click, never from Show(), so a "Type Here" box that is being
    /// typed in keeps its focus through a rebuild.</summary>
    private void FocusSelectedItemButton()
    {
        if (_selectedItemName is null) return;
        var name = "ToolStripInlineItem_" + _selectedItemName;
        this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == name)?.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // The nested DropDownItems panel (see WinFormsToolStripDropDownPanel) is itself a child
        // of this Canvas and handles its own Delete/Backspace first, setting e.Handled; this
        // backstop against a focus corner case otherwise mirrors WinFormsMenuDesignEditor's own.
        if (e.Handled || e.Source is TextBox || e.Key is not (Key.Delete or Key.Back)
            || _selectedItemName is not { } name || !_items.Any(item => item.Name == name)) return;
        DeleteItemRequested?.Invoke(name);
        e.Handled = true;
    }

    private IEnumerable<(string Display, string TypeName)> Kinds =>
        _statusStrip ? WinFormsToolStripDocument.StatusStripItemKinds : WinFormsToolStripDocument.ToolStripItemKinds;

    private void Rebuild()
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(2, 1),
        };

        foreach (var item in _items)
        {
            var button = new Button
            {
                Name = "ToolStripInlineItem_" + item.Name,
                Content = item.TypeName.Equals("ToolStripSeparator", StringComparison.OrdinalIgnoreCase)
                    ? "│" : item.Text.Length > 0 ? item.Text : item.Name,
                Background = item.Name == _selectedItemName ? Brushes.LightBlue : Brushes.Transparent,
            };
            button.Click += (_, _) =>
            {
                SetSelectedItem(item.Name);
                Rebuild();
                FocusSelectedItemButton();
            };
            var delete = new MenuItem { Name = "ToolStripInlineDelete_" + item.Name, Header = Localizer.Get(StringKeys.DesignerDelete) };
            delete.Click += (_, _) =>
            {
                SetSelectedItem(item.Name);
                DeleteItemRequested?.Invoke(item.Name);
            };
            button.ContextMenu = new ContextMenu { ItemsSource = new object[] { delete } };
            bar.Children.Add(button);
        }

        if (!_unsupported)
        {
            bar.Children.Add(Placeholder());
            var arrow = new ToggleButton
            {
                Name = "ToolStripInlineTypeArrow",
                Content = "▾",
                IsChecked = _typePickerOpen,
            };
            ToolTip.SetTip(arrow, Localizer.Get(StringKeys.WinFormsToolStripChooseType));
            arrow.Click += (_, _) =>
            {
                _typePickerOpen = !_typePickerOpen;
                Rebuild();
                FocusSelectedItemButton();
            };
            bar.Children.Add(arrow);
        }

        _bar.Child = bar;
        BuildDropdown();
    }

    private void BuildDropdown()
    {
        if (_unsupported)
        {
            Canvas.SetTop(_dropdown, 25);
            Canvas.SetLeft(_dropdown, 4);
            _dropdown.IsVisible = true;
            _dropdown.Child = new TextBlock
            {
                Text = Localizer.Get(StringKeys.WinFormsToolStripUnsupported),
                Margin = new Thickness(8),
                TextWrapping = TextWrapping.Wrap,
                Width = 230,
            };
            _dropDownHost.IsVisible = false;
            Height = 90;
            return;
        }

        // A selected DropDownButton/SplitButton shows its own cascading DropDownItems panel
        // first, directly under the bar: Visual Studio opens that submenu-editing surface the
        // moment the owning item is selected, before the plain "add a new top-level item" picker
        // below it gets a turn, so the two stack rather than compete for the same slot.
        var top = 25.0;
        if (SelectedDropDownOwner is { } owner)
        {
            _dropDownPanel.Show(owner.Name, _dropDownItems, _dropDownUnsupported);
            Canvas.SetTop(_dropDownHost, top);
            Canvas.SetLeft(_dropDownHost, 4);
            _dropDownHost.IsVisible = true;
            top += _dropDownPanel.Height + 4;
        }
        else
        {
            _dropDownHost.IsVisible = false;
        }

        if (!_typePickerOpen)
        {
            _dropdown.IsVisible = false;
            _dropdown.Child = null;
            Height = Math.Max(28, top);
            return;
        }

        var rows = new StackPanel { Spacing = Spacing.Tight, Margin = new Thickness(6), Width = 180 };
        foreach (var kind in Kinds)
        {
            var button = new Button
            {
                Name = "ToolStripInlineType_" + kind.TypeName,
                Content = Localizer.Get(DisplayKey(kind.TypeName)),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Width = 165,
            };
            button.Click += (_, _) =>
            {
                _typePickerOpen = false;
                EditRequested?.Invoke(new WinFormsToolStripDesignChange(kind.TypeName, ""));
            };
            rows.Children.Add(button);
        }

        _dropdown.Child = new ScrollViewer
        {
            Content = rows,
            MaxHeight = 280,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Canvas.SetTop(_dropdown, top);
        Canvas.SetLeft(_dropdown, 4);
        _dropdown.IsVisible = true;
        top += Math.Min(10 + Kinds.Count() * 30, 290) + 4;
        Height = Math.Max(28, top);
    }

    private static string DisplayKey(string typeName) => typeName switch
    {
        "ToolStripButton" => StringKeys.WinFormsToolStripButton,
        "ToolStripLabel" => StringKeys.WinFormsToolStripLabel,
        "ToolStripSplitButton" => StringKeys.WinFormsToolStripSplitButton,
        "ToolStripDropDownButton" => StringKeys.WinFormsToolStripDropDownButton,
        "ToolStripSeparator" => StringKeys.WinFormsToolStripSeparator,
        "ToolStripComboBox" => StringKeys.WinFormsToolStripComboBox,
        "ToolStripTextBox" => StringKeys.WinFormsToolStripTextBox,
        "ToolStripProgressBar" => StringKeys.WinFormsToolStripProgressBar,
        "ToolStripStatusLabel" => StringKeys.WinFormsToolStripStatusLabel,
        _ => StringKeys.WinFormsToolStripButton,
    };

    private TextBox Placeholder()
    {
        var input = new TextBox
        {
            Name = "ToolStripInlineTypeHere",
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
            EditRequested?.Invoke(new WinFormsToolStripDesignChange(
                WinFormsToolStripDocument.DefaultTypeName(_statusStrip), text));
            args.Handled = true;
        };
        return input;
    }
}
