using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

public sealed partial class PropertyGrid
{
    internal static readonly Color VisualStudioLineColor = WinFormsPropertyGridPalette.Light.GridLineColor;
    internal static readonly Color VisualStudioSelectionColor = WinFormsPropertyGridPalette.Light.SelectionBackgroundColor;
    private readonly StackPanel _visualStudioRows = new();
    private readonly List<VisualStudioEntry> _visualStudioEntries = [];
    private readonly HashSet<string> _expandedProperties = new(StringComparer.Ordinal);
    private readonly TextBlock _visualStudioName = new()
    {
        FontWeight = FontWeight.Bold,
        FontSize = 12
    };
    private readonly TextBlock _visualStudioDescription = new()
    {
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap
    };
    private Control _standardLayout = null!;
    private Grid _visualStudioLayout = null!;
    private StackPanel _visualStudioToolbar = null!;
    private bool _visualStudioStyle;
    private int _visualStudioEditorGeneration;
    private WinFormsPropertyGridPalette VisualStudioPalette => WinFormsPropertyGridPalette.For(ActualThemeVariant);
    private bool _eventsMode;
    private ToggleButton _visualStudioCategories = null!;
    private ToggleButton _visualStudioAlphabetical = null!;
    private ToggleButton _visualStudioProperties = null!;
    private ToggleButton _visualStudioEvents = null!;
    private Control? _eventsContent;
    private ScrollViewer _visualStudioScroll = null!;
    private Border _visualStudioDescriptionPane = null!;

    public event Action<bool>? EventsModeChanged;
    public Action? ExternalEventsRequested { get; set; }
    public Control? EventsContent
    {
        get => _eventsContent;
        set
        {
            _eventsContent = value;
            if (_eventsMode)
                _visualStudioScroll.Content = value;
        }
    }
    public bool EventsMode
    {
        get => _eventsMode;
        set
        {
            if (_eventsMode == value) return;
            _eventsMode = value;
            _visualStudioProperties.IsChecked = !value;
            _visualStudioEvents.IsChecked = value;
            _visualStudioScroll.Content = value ? _eventsContent : _visualStudioRows;
            _visualStudioDescriptionPane.IsVisible = !value;
            EventsModeChanged?.Invoke(value);
        }
    }
    private string? _visualStudioSelectedName;

    private sealed record VisualStudioEntry(DesignableProperty Property, Border Row, Border Label, TextBlock LabelText, Control Editor);

    public bool VisualStudioStyle
    {
        get => _visualStudioStyle;
        set
        {
            if (_visualStudioStyle == value) return;
            _visualStudioStyle = value;
            Content = value ? _visualStudioLayout : _standardLayout;
            Rebuild();
        }
    }

    public bool ShowVisualStudioToolbar
    {
        get => _visualStudioToolbar.IsVisible;
        set => _visualStudioToolbar.IsVisible = value;
    }

    public Func<DesignableProperty, bool>? CanRequestPropertyEditor { get; set; }
    public event Action<DesignableProperty>? PropertyEditorRequested;
    public event Action<DesignableProperty, bool>? PropertyExpansionRequested;
    public event Action<DesignableProperty, string?>? NestedPropertyEdited;
    public string? SelectedPropertyName => _visualStudioSelectedName;

    public void SetSortOrder(SortOrder order)
    {
        _sortByCategory.IsChecked = order == SortOrder.ByCategory;
        UpdateVisualStudioToolbar();
    }

    private void UpdateVisualStudioToolbar()
    {
        _visualStudioCategories.IsChecked = Order == SortOrder.ByCategory;
        _visualStudioAlphabetical.IsChecked = Order == SortOrder.Alphabetical;
        _visualStudioProperties.IsChecked = !EventsMode;
        _visualStudioEvents.IsChecked = EventsMode;
    }

    private static ToggleButton CreateVisualStudioToolbarButton(string name, string geometry, string tooltipKey)
    {
        return new ToggleButton
        {
            Name = name,
            Content = VisualStudioIcon(geometry),
            Width = 25,
            Height = 24,
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(Spacing.Tight),
            [ToolTip.TipProperty] = Localizer.Get(tooltipKey),
        };
    }

    private void InitializeVisualStudioGrid(Control standardLayout)
    {
        _standardLayout = standardLayout;
        _visualStudioCategories = CreateVisualStudioToolbarButton(
            "PropertyGridCategorized", "M1,1 H13 M1,5 H13 M1,9 H13", StringKeys.PropertiesSortByCategory);
        _visualStudioAlphabetical = CreateVisualStudioToolbarButton(
            "PropertyGridAlphabetical", "M1,10 L4,1 L7,10 M2,7 H6 M10,1 V10 M8,8 L10,10 L12,8", "PropertyGrid_SortAlphabetical");
        _visualStudioProperties = CreateVisualStudioToolbarButton(
            "PropertyGridProperties", "M1,1 H12 V11 H1 Z M4,1 V11 M1,4 H12 M1,7 H12", "WinFormsDesigner_Properties");
        _visualStudioEvents = CreateVisualStudioToolbarButton(
            "PropertyGridEvents", "M8,0 L2,7 H6 L4,13 L12,5 H8 Z", "WinFormsDesigner_Events");

        _visualStudioCategories.Click += (_, _) => SetSortOrder(SortOrder.ByCategory);
        _visualStudioAlphabetical.Click += (_, _) => SetSortOrder(SortOrder.Alphabetical);
        _visualStudioProperties.Click += (_, _) =>
        {
            EventsMode = false;
            UpdateVisualStudioToolbar();
        };
        _visualStudioEvents.Click += (_, _) =>
        {
            if (ExternalEventsRequested is { } openEvents)
            {
                openEvents();
                UpdateVisualStudioToolbar();
                return;
            }
            EventsMode = true;
            UpdateVisualStudioToolbar();
        };

        _visualStudioToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            Margin = new Thickness(2),
            Children = { _visualStudioCategories, _visualStudioAlphabetical, _visualStudioProperties, _visualStudioEvents },
        };
        var description = _visualStudioDescriptionPane = new Border
        {
            Name = "PropertyGridDescription",
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = VisualStudioPalette.GridLineBrush,
            MinHeight = 65,
            Padding = new Thickness(Spacing.Small, Spacing.Tight),
            Child = new StackPanel
            {
                Spacing = Spacing.Tight,
                Children = { _visualStudioName, _visualStudioDescription }
            },
        };
        var scroll = _visualStudioScroll = new ScrollViewer
        {
            Content = _visualStudioRows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _visualStudioLayout = new Grid
        {
            Background = VisualStudioPalette.BackgroundBrush,
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children = { _visualStudioToolbar, scroll, description },
        };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(description, 2);
        ActualThemeVariantChanged += (_, _) => RefreshVisualStudioTheme();
        UpdateVisualStudioThemeChrome();
    }

    private static Control VisualStudioIcon(string geometry) => new Avalonia.Controls.Shapes.Path
    {
        Data = Geometry.Parse(geometry),
        Stroke = Brushes.Black,
        StrokeThickness = 1,
        Width = 14,
        Height = 14,
        Stretch = Stretch.Uniform,
    };

    private void RebuildVisualStudioGrid()
    {
        UpdateVisualStudioToolbar();
        UpdateVisualStudioThemeChrome();
        ++_visualStudioEditorGeneration;
        _visualStudioRows.Children.Clear();
        _visualStudioEntries.Clear();
        var properties = _properties.Where(property => property.ParentName is null
            || _expandedProperties.Contains(property.ParentName)).ToArray();
        if (Order == SortOrder.Alphabetical)
        {
            foreach (var property in OrderedVisualStudioProperties(properties)) AddVisualStudioRow(property);
        }
        else
        {
            foreach (var category in properties.GroupBy(property => property.CategoryName ?? NameOf(property.Category))
                .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                // Named metadata categories may share the same fallback enum category.
                var categoryName = category.Key;
                var expanded = !_visualStudioCollapsedCategories.Contains(categoryName);
                var heading = new Button
                {
                    Name = "PropertyGridCategory",
                    Content = (expanded ? "−  " : "+  ") + categoryName,
                    Height = 21,
                    MinHeight = 0,
                    Padding = new Thickness(4, 0),
                    CornerRadius = new CornerRadius(0),
                    Background = VisualStudioPalette.CategoryBackgroundBrush,
                    Foreground = VisualStudioPalette.ForegroundBrush,
                    FontWeight = FontWeight.Bold,
                    FontSize = 11,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    BorderBrush = VisualStudioPalette.GridLineBrush,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                heading.Click += (_, _) =>
                {
                    if (!_visualStudioCollapsedCategories.Remove(categoryName)) _visualStudioCollapsedCategories.Add(categoryName);
                    Rebuild();
                };
                _visualStudioRows.Children.Add(heading);
                if (expanded)
                    foreach (var property in OrderedVisualStudioProperties(category)) AddVisualStudioRow(property);
            }
        }
        if (_visualStudioEntries.Count == 0)
            _visualStudioRows.Children.Add(new TextBlock
            {
                Text = Localizer.Get(_properties.Count == 0 ? StringKeys.PropertiesNoSelection : StringKeys.PropertiesNoMatch),
                Margin = new Thickness(Spacing.Tight),
            });
        var selected = _visualStudioEntries.FirstOrDefault(entry => VisualStudioPropertyKey(entry.Property) == _visualStudioSelectedName);
        if (selected is not null) SelectVisualStudioProperty(selected, false);
        else
        {
            _visualStudioName.Text = "";
            _visualStudioDescription.Text = "";
        }
    }

    private readonly HashSet<string> _visualStudioCollapsedCategories = new(StringComparer.Ordinal);

    private static IEnumerable<DesignableProperty> OrderedVisualStudioProperties(IEnumerable<DesignableProperty> properties)
    {
        var all = properties.ToArray();
        foreach (var root in all.Where(property => property.ParentName is null).OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return root;
            foreach (var child in all.Where(property => property.ParentName == root.Name).OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
                yield return child;
        }
    }

}
