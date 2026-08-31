using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

/// <summary>
/// A grid of properties, in the shape Visual Basic developers expect.
/// </summary>
/// <remarks>
/// Two columns with a draggable splitter, collapsible category headings, a
/// search box, and the selected property explained at the bottom.
///
/// Independent of the designer on purpose: it takes a list of properties and
/// reports changes, so the same control can show a Visual Basic form's
/// properties one day without being untangled from XAML first.
/// </remarks>
public sealed class PropertyGrid : UserControl
{
    /// <summary>How the properties are ordered.</summary>
    public enum SortOrder
    {
        /// <summary>Grouped under their category, as Visual Studio opens.</summary>
        ByCategory,

        /// <summary>One flat alphabetical list.</summary>
        Alphabetical,
    }

    private readonly StackPanel _rows;
    private readonly TextBox _search;
    private readonly TextBlock _selectedName;
    private readonly TextBlock _selectedDescription;
    private readonly ToggleButton _sortByCategory;

    private IReadOnlyList<DesignableProperty> _properties = [];
    private readonly HashSet<PropertyCategory> _collapsed = [];
    private GridLength _nameWidth = new(120, GridUnitType.Pixel);

    /// <summary>Raised when a property is given a new value, or reset with null.</summary>
    /// <remarks>
    /// Not PropertyChanged: AvaloniaObject has an event by that name, and one
    /// here hid it. Anyone subscribing to grid.PropertyChanged expecting
    /// Avalonia's — to watch the control's own properties — would have been
    /// handed this one instead, and the compiler's only complaint is a
    /// warning nobody reads twice.
    /// </remarks>
    public event Action<string, string?>? PropertyEdited;

    public PropertyGrid()
    {
        _search = new TextBox
        {
            PlaceholderText = Localizer.Get(StringKeys.PropertiesSearch),
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            FontSize = 12,
        };

        _search.TextChanged += (_, _) => Rebuild();

        _sortByCategory = new ToggleButton
        {
            // Drawn rather than typed: a glyph renders as whatever the
            // system font has for it, which on this machine was a solid
            // rectangle where the three bars should be.
            Content = GroupIcon(),
            IsChecked = true,
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, Spacing.Tight, Spacing.Normal, Spacing.Tight),
            [ToolTip.TipProperty] = Localizer.Get(StringKeys.PropertiesSortByCategory),
        };

        _sortByCategory.IsCheckedChanged += (_, _) => Rebuild();

        _rows = new StackPanel();

        _selectedName = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
        };

        _selectedDescription = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
        };

        // The search box must not stretch under the sort button: with a
        // single starred column the button was pushed past the right edge and
        // rendered half outside the panel.
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, Spacing.Tight, 0),
            Children = { _search, _sortByCategory },
        };

        Grid.SetColumn(_sortByCategory, 1);

        // The description sits at the bottom, as in Visual Studio, and keeps
        // its height whether or not anything is selected: a panel that grows
        // and shrinks as the pointer moves is hard to read.
        var description = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            Padding = new Thickness(Spacing.Normal, Spacing.Tight),
            MinHeight = 52,
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { _selectedName, _selectedDescription },
            },
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                header,
                new ScrollViewer
                {
                    Content = _rows,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
                description,
            },
        };

        Grid.SetRow(header, 0);
        Grid.SetRow((Control)layout.Children[1], 1);
        Grid.SetRow(description, 2);

        Content = layout;
    }

    /// <summary>What the grid shows.</summary>
    public IReadOnlyList<DesignableProperty> Properties
    {
        get => _properties;
        set
        {
            _properties = value ?? [];
            Rebuild();
        }
    }

    /// <summary>The name shown above the properties, usually the element's.</summary>
    public string? SubjectName { get; set; }

    /// <summary>Which ordering the grid is using.</summary>
    public SortOrder Order =>
        _sortByCategory.IsChecked == true ? SortOrder.ByCategory : SortOrder.Alphabetical;

    /// <summary>The rows currently shown, for tests.</summary>
    internal IReadOnlyList<Control> Rows => [.. _rows.Children];

    private void Rebuild()
    {
        _rows.Children.Clear();

        var filtered = Filtered();

        if (filtered.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = Localizer.Get(_properties.Count == 0
                    ? StringKeys.PropertiesNoSelection
                    : StringKeys.PropertiesNoMatch),
                Opacity = 0.6,
                FontSize = 12,
                Margin = new Thickness(Spacing.Normal, Spacing.Normal),
            });

            return;
        }

        if (Order == SortOrder.Alphabetical)
        {
            foreach (var property in filtered.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                _rows.Children.Add(BuildRow(property));

            return;
        }

        foreach (var group in filtered.GroupBy(p => p.Category).OrderBy(g => g.Key))
        {
            _rows.Children.Add(BuildCategoryHeader(group.Key, group.Count()));

            if (_collapsed.Contains(group.Key)) continue;

            foreach (var property in group.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                _rows.Children.Add(BuildRow(property));
        }
    }

    private IReadOnlyList<DesignableProperty> Filtered()
    {
        var term = _search.Text?.Trim();

        if (string.IsNullOrEmpty(term)) return _properties;

        return [.. _properties.Where(p =>
            p.Name.Contains(term, StringComparison.OrdinalIgnoreCase))];
    }

    private Control BuildCategoryHeader(PropertyCategory category, int count)
    {
        var collapsed = _collapsed.Contains(category);

        var button = new Button
        {
            Background = new SolidColorBrush(Color.FromArgb(25, 128, 128, 128)),
            BorderThickness = new Thickness(0),
            Padding = Spacing.RowPadding,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(0),
            Content = new TextBlock
            {
                Text = $"{(collapsed ? "▸" : "▾")}  {NameOf(category)}  ({count})",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
            },
        };

        button.Click += (_, _) =>
        {
            if (!_collapsed.Remove(category)) _collapsed.Add(category);

            Rebuild();
        };

        return button;
    }

    private static string NameOf(PropertyCategory category) => Localizer.Get(category switch
    {
        PropertyCategory.Layout => StringKeys.PropertiesCategoryLayout,
        PropertyCategory.Appearance => StringKeys.PropertiesCategoryAppearance,
        PropertyCategory.Behavior => StringKeys.PropertiesCategoryBehavior,
        _ => StringKeys.PropertiesCategoryCommon,
    });

    private Control BuildRow(DesignableProperty property)
    {
        var label = new TextBlock
        {
            Text = property.Name,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Spacing.Normal, 0, Spacing.Tight, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,

            // A value the template sets is shown in bold: without the
            // distinction there is no telling what the file says from what the
            // control happens to default to.
            FontWeight = property.IsSet ? FontWeight.SemiBold : FontWeight.Normal,
        };

        var editor = BuildEditor(property);

        var splitter = new GridSplitter
        {
            Width = 4,
            Background = Brushes.Transparent,
            ResizeDirection = GridResizeDirection.Columns,
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(_nameWidth) { MinWidth = 60 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Margin = new Thickness(0, 1, Spacing.Tight, 1),
            MinHeight = 24,
            Children = { label, splitter, editor },
        };

        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(editor, 2);

        // Dragging the splitter on one row moves them all: two columns that
        // only line up sometimes are worse than one width for everybody.
        row.ColumnDefinitions[0].PropertyChanged += (_, change) =>
        {
            if (change.Property.Name != nameof(ColumnDefinition.Width)) return;

            _nameWidth = row.ColumnDefinitions[0].Width;

            foreach (var other in _rows.Children.OfType<Grid>())
                if (!ReferenceEquals(other, row) && other.ColumnDefinitions.Count == 3)
                    other.ColumnDefinitions[0].Width = _nameWidth;
        };

        row.PointerPressed += (_, _) => Describe(property);
        label.PointerPressed += (_, _) => Describe(property);

        return row;
    }

    private void Describe(DesignableProperty property)
    {
        _selectedName.Text = property.Name;

        _selectedDescription.Text = property.Description;
    }

    private Control BuildEditor(DesignableProperty property) => property.EditorKind switch
    {
        PropertyEditorKind.Boolean => BuildBoolean(property),
        PropertyEditorKind.Enumeration => BuildEnumeration(property),
        PropertyEditorKind.Number => BuildNumber(property),
        PropertyEditorKind.Brush => BuildBrush(property),
        PropertyEditorKind.Thickness => BuildThickness(property),
        _ => BuildText(property),
    };

    private Control BuildBoolean(DesignableProperty property)
    {
        // Sized down from the platform default: a tick here is one row of
        // forty in a panel, not a decision the user opened a dialog for, and
        // at its natural size it makes its row taller than every other.
        var box = new CheckBox
        {
            IsChecked = bool.TryParse(property.CurrentValue, out var value) && value,
            Margin = new Thickness(Spacing.Tight, 0),
            MinHeight = 0,

            // No local height: a value set here beats the style, and the
            // style is where the size of a check box is decided for the whole
            // IDE. Setting 22 from code is what kept them the tallest thing
            // in the row after the style said 18.
            VerticalAlignment = VerticalAlignment.Center,
        };

        box.IsCheckedChanged += (_, _) => Set(property, box.IsChecked?.ToString());

        return box;
    }

    private Control BuildEnumeration(DesignableProperty property)
    {
        var combo = new ComboBox
        {
            ItemsSource = property.AllowedValues,
            SelectedItem = property.CurrentValue,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 11,
            MinHeight = 0,
            Height = Spacing.EditorHeight,
            Padding = Spacing.RowPadding,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        combo.SelectionChanged += (_, _) => Set(property, combo.SelectedItem as string);

        return WithReset(property, combo);
    }

    private Control BuildNumber(DesignableProperty property)
    {
        // The same height as every other editor, and the spinner off: two
        // arrows take a third of the width of a narrow column, and a number
        // in a property grid is typed far more often than nudged.
        var box = new NumericUpDown
        {
            Value = decimal.TryParse(property.CurrentValue, out var value) ? value : null,
            FontSize = 11,
            MinHeight = 0,
            Height = Spacing.EditorHeight,
            Padding = Spacing.RowPadding,
            Increment = 1,
            ShowButtonSpinner = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        box.ValueChanged += (_, _) =>
            Set(property, box.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return WithReset(property, box);
    }

    private Control BuildBrush(DesignableProperty property)
    {
        var box = new TextBox
        {
            Text = property.CurrentValue ?? "",
            FontSize = 11,
            MinHeight = 0,
            Padding = Spacing.RowPadding,
        };

        // A swatch of the colour beside the text: a hex string says nothing
        // about what it looks like, which is the whole question being asked.
        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(Spacing.Tight, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            Background = ParseBrush(property.CurrentValue),
        };

        box.TextChanged += (_, _) => swatch.Background = ParseBrush(box.Text);
        box.LostFocus += (_, _) => Set(property, Blank(box.Text));

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { box, swatch },
        };

        Grid.SetColumn(swatch, 1);

        return WithReset(property, row);
    }

    /// <summary>The brush a value names, or none when it names nothing.</summary>
    private static IBrush? ParseBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            return Brush.Parse(value);
        }
        catch (Exception)
        {
            // Half-typed colours are the normal state of a text box being
            // edited, so this is not worth reporting.
            return null;
        }
    }

    private Control BuildThickness(DesignableProperty property)
    {
        var parts = (property.CurrentValue ?? "").Split(',', StringSplitOptions.TrimEntries);

        var boxes = new TextBox[4];
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };

        for (var i = 0; i < 4; i++)
        {
            // "8" means all four sides, "4,2" means horizontal then vertical:
            // the shorthand XAML allows, expanded so each field shows its own.
            var text = parts.Length switch
            {
                1 when parts[0].Length > 0 => parts[0],
                2 => parts[i % 2],
                4 => parts[i],
                _ => "",
            };

            var box = new TextBox
            {
                Text = text,
                FontSize = 11,
                MinHeight = 0,
                Padding = new Thickness(4, 2),
                Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0),
                [ToolTip.TipProperty] = new[] { "Left", "Top", "Right", "Bottom" }[i],
            };

            boxes[i] = box;
            row.Children.Add(box);
            Grid.SetColumn(box, i);
        }

        foreach (var box in boxes)
        {
            box.LostFocus += (_, _) =>
            {
                var written = string.Join(",", boxes.Select(b => Blank(b.Text) ?? "0"));

                Set(property, written == "0,0,0,0" ? null : written);
            };
        }

        return WithReset(property, row);
    }

    private Control BuildText(DesignableProperty property)
    {
        var box = new TextBox
        {
            Text = property.CurrentValue ?? "",
            FontSize = 11,
            MinHeight = 0,
            Padding = Spacing.RowPadding,
        };

        box.LostFocus += (_, _) => Set(property, Blank(box.Text));

        return WithReset(property, box);
    }

    /// <summary>
    /// The editor with a reset button beside it, when the property is set.
    /// </summary>
    /// <remarks>
    /// Clearing a text box is not the same as resetting: an empty string is a
    /// value, and some properties mean something different when set to it.
    /// </remarks>
    private Control WithReset(DesignableProperty property, Control editor)
    {
        if (!property.IsSet) return editor;

        var reset = new Button
        {
            Content = ResetIcon(),
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = Localizer.Get(StringKeys.PropertiesReset),
        };

        // Named so a test can find it: the content is a drawing, and
        // searching the tree for a shape is a test about drawings rather than
        // about resetting.
        reset.Name = "PART_Reset";

        reset.Click += (_, _) => Set(property, null);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { editor, reset },
        };

        Grid.SetColumn(reset, 1);

        return row;
    }

    /// <summary>Three bars, for the group-by-category toggle.</summary>
    /// <remarks>
    /// Drawn as a Path so the theme's own foreground applies: a Border with a
    /// fixed brush is invisible against one of the two backgrounds a toggle
    /// has, and the button read as a plain blue rectangle whichever colour
    /// was picked.
    /// </remarks>
    private static Control GroupIcon() =>
        new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 H12 M0,4 H12 M0,8 H12"),
            StrokeThickness = 1.5,
            [!Avalonia.Controls.Shapes.Shape.StrokeProperty] =
                new Binding(nameof(ToggleButton.Foreground))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                    {
                        AncestorType = typeof(ToggleButton),
                    },
                },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

    /// <summary>A cross, for the reset button.</summary>
    /// <remarks>
    /// Two rotated bars rather than the ⨯ character: rendered, the glyph came
    /// out as a small faded asterisk that read as decoration rather than as
    /// something to click.
    /// </remarks>
    private static Control ResetIcon()
    {
        var cross = new Canvas { Width = 9, Height = 9 };

        foreach (var angle in new[] { 45d, -45d })
        {
            var bar = new Border
            {
                Width = 9,
                Height = 1.5,
                Background = Brushes.Gray,
                RenderTransform = new RotateTransform(angle),
            };

            Canvas.SetTop(bar, 3.75);
            cross.Children.Add(bar);
        }

        return cross;
    }

    private static string? Blank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text;

    private void Set(DesignableProperty property, string? value)
    {
        if (value == property.CurrentValue) return;

        PropertyEdited?.Invoke(property.Name, value);
    }
}
