using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

/// <summary>Edits event bindings while the document owner persists and navigates to handlers.</summary>
public sealed class WinFormsEventPanel : UserControl
{
    private readonly StackPanel _rows = new();
    private readonly TextBlock _hint;
    private readonly TextBlock _eventHeader;
    private readonly TextBlock _handlerHeader;
    private readonly TextBlock _descriptionName = new()
    {
        FontWeight = FontWeight.Bold,
        FontSize = WinFormsPropertyGridPalette.FontSize,
    };
    private readonly TextBlock _descriptionText = new()
    {
        FontSize = WinFormsPropertyGridPalette.FontSize,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Border _description;
    private readonly Grid _header;
    private readonly ScrollViewer _scroll;
    private readonly HashSet<string> _collapsedCategories = new(StringComparer.Ordinal);
    private Grid? _selectedRow;
    private object? _control;
    private Func<string, string?>? _existingHandler;
    private Action<string, string?>? _handlerRequested;
    private Func<string, string>? _suggestHandlerName;
    private int _rowGeneration;
    private bool _visualStudioStyle;

    /// <summary>Shows event categories, a compact two-column grid, and selection help.</summary>
    public bool VisualStudioStyle
    {
        get => _visualStudioStyle;
        set
        {
            if (_visualStudioStyle == value)
            {
                return;
            }

            _visualStudioStyle = value;
            Rebuild();
        }
    }

    public WinFormsEventPanel()
    {
        _hint = new TextBlock
        {
            Text = Localizer.Get(StringKeys.WinFormsDesignerEventsHint),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            FontSize = WinFormsPropertyGridPalette.FontSize,
        };
        _header = new Grid
        {
            // The property grid's default name column: Event and Handler line up with Name and Value.
            ColumnDefinitions = new ColumnDefinitions("120,3,*"),
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
        };
        _eventHeader = new TextBlock
        {
            Text = Localizer.Get(StringKeys.WinFormsDesignerEvent),
            FontWeight = FontWeight.SemiBold,
            FontSize = WinFormsPropertyGridPalette.FontSize,
        };
        _header.Children.Add(_eventHeader);
        _handlerHeader = new TextBlock
        {
            Text = Localizer.Get(StringKeys.WinFormsDesignerHandler),
            FontWeight = FontWeight.SemiBold,
            FontSize = WinFormsPropertyGridPalette.FontSize,
        };
        _header.Children.Add(_handlerHeader);
        Grid.SetColumn(_handlerHeader, 2);
        var layout = new DockPanel();
        layout.Children.Add(_hint);
        layout.Children.Add(_header);
        DockPanel.SetDock(_hint, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(_header, Avalonia.Controls.Dock.Top);
        _description = new Border
        {
            Name = "EventDescription",
            BorderThickness = new Thickness(0, 1, 0, 0),
            MinHeight = 65,
            Padding = new Thickness(Spacing.Normal, Spacing.Tight),
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { _descriptionName, _descriptionText },
            },
        };
        layout.Children.Add(_description);
        DockPanel.SetDock(_description, Avalonia.Controls.Dock.Bottom);
        _scroll = new ScrollViewer
        {
            Content = _rows,
        };
        layout.Children.Add(_scroll);
        Content = layout;
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AttachedToVisualTree += (_, _) => ApplyPalette();
        PropertyChanged += (_, change) =>
        {
            // Focus only changes which row wears the selection fill.
            if (change.Property == IsKeyboardFocusWithinProperty)
                ApplySelection();
        };
        ApplyPalette();
    }

    /// <param name="handlerRequested">Receives an assignment or null removal. Double-click also requests navigation.</param>
    public void Show(object? control, Func<string, string?>? existingHandler = null,
        Action<string, string?>? handlerRequested = null, Func<string, string>? suggestHandlerName = null)
    {
        _control = control;
        _existingHandler = existingHandler;
        _handlerRequested = handlerRequested;
        _suggestHandlerName = suggestHandlerName;
        Rebuild();
    }

    private void Rebuild()
    {
        // Removing a focused editor can deliver LostFocus after the selection
        // callback has changed. Retired rows must never edit the new object.
        _rowGeneration++;
        _rows.Children.Clear();
        _selectedRow = null;
        _descriptionName.Text = "";
        _descriptionText.Text = "";
        _description.IsVisible = _visualStudioStyle;
        _hint.IsVisible = !_visualStudioStyle;
        _header.IsVisible = _control is not null;
        if (_control is null)
        {
            _rows.Children.Add(new TextBlock
            {
                Name = "EventsEmptyState",
                Text = Localizer.Get(StringKeys.EventsNothingSelected),
                FontSize = WinFormsPropertyGridPalette.FontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Spacing.Normal, Spacing.Normal),
            });
            ApplyPalette();
            return;
        }

        var descriptors = WinFormsEventInspector.Describe(_control, _existingHandler);
        if (_visualStudioStyle)
        {
            foreach (var category in descriptors.GroupBy(descriptor => descriptor.Category)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                _rows.Children.Add(BuildCategory(category.Key));
                if (_collapsedCategories.Contains(category.Key))
                {
                    continue;
                }

                foreach (var descriptor in category.OrderByDescending(item => item.IsDefault)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                {
                    _rows.Children.Add(BuildRow(descriptor));
                }
            }

            ApplyPalette();
            return;
        }

        foreach (var descriptor in descriptors)
        {
            _rows.Children.Add(BuildRow(descriptor));
        }

        ApplyPalette();
    }

    private Control BuildCategory(string category)
    {
        var palette = Palette();
        var button = new Button
        {
            Name = "EventCategory_" + category,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = palette.GridLineBrush,
            CornerRadius = new CornerRadius(0),
            Height = WinFormsPropertyGridPalette.RowHeight,
            MinHeight = 0,
            Padding = new Thickness(4, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock
            {
                Text = (_collapsedCategories.Contains(category) ? "+  " : "−  ") + category,
                FontSize = WinFormsPropertyGridPalette.FontSize,
                FontWeight = FontWeight.Bold,
            },
        };
        palette.StyleButton(button, palette.CategoryBackgroundBrush);
        button.Click += (_, _) =>
        {
            if (!_collapsedCategories.Remove(category))
            {
                _collapsedCategories.Add(category);
            }

            Rebuild();
        };
        return button;
    }

    private Control BuildRow(WinFormsDesignableEvent descriptor)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(_visualStudioStyle ? "120,3,*" : "*,*"),
            Margin = _visualStudioStyle ? new Thickness(0) : new Thickness(Spacing.Normal, 1),
            MinHeight = _visualStudioStyle ? WinFormsPropertyGridPalette.RowHeight : 0,
            Background = Brushes.Transparent,
        };
        var label = new TextBlock
        {
            Text = descriptor.Name,
            FontWeight = descriptor.IsDefault ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = WinFormsPropertyGridPalette.FontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = _visualStudioStyle ? new Thickness(Spacing.Normal, 0, 2, 0) : new Thickness(0),
            [ToolTip.TipProperty] = descriptor.Description ?? descriptor.EventType.Name,
        };
        var editor = new TextBox
        {
            Text = descriptor.HandlerName ?? "",
            FontSize = WinFormsPropertyGridPalette.FontSize,
            MinHeight = 0,
            Height = _visualStudioStyle ? 20 : Spacing.EditorHeight,
            Padding = _visualStudioStyle ? new Thickness(2, 0) : Spacing.RowPadding,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = _visualStudioStyle ? new Thickness(0) : new Thickness(1),
            [ToolTip.TipProperty] = descriptor.EventType.Name,
        };
        string? committed = descriptor.HandlerName;
        var generation = _rowGeneration;
        var handlerRequested = _handlerRequested;
        var suggestHandlerName = _suggestHandlerName;
        var controlTypeName = _control!.GetType().Name;

        void Commit()
        {
            if (generation != _rowGeneration)
            {
                return;
            }

            var value = string.IsNullOrWhiteSpace(editor.Text) ? null : editor.Text.Trim();
            if (value == committed)
            {
                return;
            }

            handlerRequested?.Invoke(descriptor.Name, value);
            committed = value;
        }

        editor.LostFocus += (_, _) => Commit();
        editor.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                Commit();
            }
            else if (args.Key == Key.Escape)
            {
                editor.Text = committed ?? "";
            }
            else
            {
                return;
            }

            args.Handled = true;
        };
        row.DoubleTapped += (_, args) =>
        {
            if (generation != _rowGeneration)
            {
                return;
            }

            var value = string.IsNullOrWhiteSpace(editor.Text)
                ? suggestHandlerName?.Invoke(descriptor.Name) ?? controlTypeName + "_" + descriptor.Name
                : editor.Text.Trim();
            // Do not rewrite the editor before the owner confirms the edit.
            // A rejected handler name must not look like a saved binding.
            handlerRequested?.Invoke(descriptor.Name, value);
            args.Handled = true;
        };
        if (_visualStudioStyle)
        {
            row.PointerPressed += (_, _) => Describe(descriptor, row);
            editor.GotFocus += (_, _) => Describe(descriptor, row);
        }

        row.Children.Add(label);
        row.Children.Add(editor);
        if (_visualStudioStyle)
        {
            // The same split and hairlines as the property grid beside it.
            var splitter = new Border { Name = "EventSplitter" };
            var line = new Border
            {
                Name = "EventRowLine",
                Height = 1,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            };
            Grid.SetColumn(splitter, 1);
            Grid.SetColumnSpan(line, 3);
            row.Children.Add(splitter);
            row.Children.Add(line);
            Grid.SetColumn(editor, 2);
        }
        else
        {
            Grid.SetColumn(editor, 1);
        }

        return row;
    }

    private void Describe(WinFormsDesignableEvent descriptor, Grid row)
    {
        _selectedRow = row;
        _descriptionName.Text = descriptor.Name;
        _descriptionText.Text = descriptor.Description ?? descriptor.EventType.Name;
        ApplySelection();
    }

    // Built once per theme: a fresh palette was made on every focus change and
    // every row click, each one 13 brushes and a rewrite of every row's colours.
    private WinFormsPropertyGridPalette? _palette;

    private WinFormsPropertyGridPalette Palette()
    {
        var fresh = WinFormsPropertyGridPalette.For(this);
        if (_palette is null || !_palette.SameAs(fresh)) _palette = fresh;
        return _palette;
    }

    /// <summary>The selected row's fill and label: the one thing focus and clicks change.</summary>
    private void ApplySelection()
    {
        var palette = _palette ?? Palette();
        var focused = IsKeyboardFocusWithin;

        foreach (var row in _rows.Children.OfType<Grid>())
        {
            var selected = ReferenceEquals(row, _selectedRow);
            row.Background = !selected ? palette.BackgroundBrush
                : focused ? palette.SelectionBackgroundBrush : palette.InactiveSelectionBackgroundBrush;
            if (row.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
                label.Foreground = selected && focused ? palette.SelectionForegroundBrush : palette.ForegroundBrush;
        }
    }

    /// <summary>Everything the theme colours: on a theme change, on attach and after a rebuild.</summary>
    private void ApplyPalette()
    {
        var palette = Palette();
        palette.InstallOn(this);
        Background = palette.BackgroundBrush;
        _scroll.Background = palette.BackgroundBrush;
        _header.Background = palette.CategoryBackgroundBrush;
        _hint.Foreground = palette.ForegroundBrush;
        _eventHeader.Foreground = palette.ForegroundBrush;
        _handlerHeader.Foreground = palette.ForegroundBrush;
        _description.Background = palette.CategoryBackgroundBrush;
        _description.BorderBrush = palette.GridLineBrush;
        _descriptionName.Foreground = palette.ForegroundBrush;
        _descriptionText.Foreground = palette.ReadOnlyForegroundBrush;

        foreach (var empty in _rows.Children.OfType<TextBlock>())
        {
            empty.Foreground = palette.ReadOnlyForegroundBrush;
        }

        foreach (var category in _rows.Children.OfType<Button>())
        {
            palette.StyleButton(category, palette.CategoryBackgroundBrush);
            category.BorderBrush = palette.GridLineBrush;
        }

        foreach (var row in _rows.Children.OfType<Grid>())
        {
            foreach (var line in row.Children.OfType<Border>())
            {
                line.Background = palette.GridLineBrush;
            }

            if (row.Children.OfType<TextBox>().FirstOrDefault() is { } editor)
            {
                // The handler cell stays on the grid surface while its row is selected,
                // exactly as the property grid's value column does.
                editor.Background = _visualStudioStyle ? palette.BackgroundBrush : palette.InputBackgroundBrush;
                editor.Foreground = palette.ForegroundBrush;
            }
        }

        ApplySelection();
    }
}
