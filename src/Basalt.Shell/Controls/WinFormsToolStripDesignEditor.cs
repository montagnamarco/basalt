using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
    private IReadOnlyList<WinFormsToolStripItem> _items = [];
    private string _strip = "";
    private bool _statusStrip;
    private bool _typePickerOpen;
    private bool _unsupported;
    private string? _selectedItemName;

    internal event Action<WinFormsToolStripDesignChange>? EditRequested;

    internal event Action<string?>? SelectedItemChanged;

    internal string? SelectedItemName => _selectedItemName;

    internal WinFormsToolStripDesignEditor()
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

    private void SetSelectedItem(string? name)
    {
        if (_selectedItemName == name)
        {
            return;
        }
        _selectedItemName = name;
        SelectedItemChanged?.Invoke(name);
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
            };
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
            };
            bar.Children.Add(arrow);
        }

        _bar.Child = bar;
        Canvas.SetTop(_dropdown, 25);
        Canvas.SetLeft(_dropdown, 4);
        BuildDropdown();
        Height = _dropdown.IsVisible ? Math.Min(30 + Kinds.Count() * 30, 310) : 28;
    }

    private void BuildDropdown()
    {
        if (_unsupported)
        {
            _dropdown.IsVisible = true;
            _dropdown.Child = new TextBlock
            {
                Text = Localizer.Get(StringKeys.WinFormsToolStripUnsupported),
                Margin = new Thickness(8),
                TextWrapping = TextWrapping.Wrap,
                Width = 230,
            };
            return;
        }

        if (!_typePickerOpen)
        {
            _dropdown.IsVisible = false;
            _dropdown.Child = null;
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
        _dropdown.IsVisible = true;
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
