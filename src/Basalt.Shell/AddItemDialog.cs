using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Shell;

/// <summary>
/// The template picked in Add New Item, and the file name typed for it.
/// <see cref="Misfit"/> is what the project lacks for it, or null when it fits:
/// the item is still added, with a warning, rather than refused.
/// </summary>
public sealed record AddItemChoice(ItemTemplateDefinition Template, string FileName, ItemRequirement? Misfit = null);

/// <summary>
/// Solution Explorer's Add New Item, on the model of Visual Studio's: a tree of
/// categories, the templates of the one chosen (or of all, while searching)
/// with an icon, a name and a description, the description and a read-only
/// preview of the file on the right, and the name below.
///
/// It checks the name as it is typed: that it can be one (an identifier for a
/// code item), and that no file it would write is already in the folder, so the
/// message comes before the click rather than after. A template that does not
/// fit the project stays in the list, marked; choosing it is allowed and the
/// owner warns when the file is added.
///
/// Wide, the description and preview sit to the right of the list; narrow, they
/// move below it, so nothing overlaps down to the minimum width.
/// </summary>
public sealed class AddItemDialog : Window
{
    /// <summary>Below this width the description and preview move under the list.</summary>
    internal const double NarrowBelow = 840;

    private const double DetailWidth = 280;
    private const double CategoryWidth = 168;

    private readonly IReadOnlyList<ItemTemplateDefinition> _templates;
    private readonly ItemProjectProfile _profile;
    private readonly string _directory;

    private readonly TreeView _categories = new() { Name = "AddItemCategories" };
    private readonly ListBox _list = new() { Name = "AddItemTemplates" };
    private readonly TextBox _search = new() { Name = "AddItemSearch" };
    private readonly TextBox _name = new() { Name = "AddItemName" };
    private readonly TextBlock _error = new() { Name = "AddItemError", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _empty = new() { Name = "AddItemNoMatches", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _description = new() { Name = "AddItemDescription", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _misfit = new() { Name = "AddItemMisfit", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _files = new() { Name = "AddItemFiles", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _preview = new() { Name = "AddItemPreview" };
    private readonly Button _create;
    private readonly Button _cancel;

    private readonly Grid _body = new() { Name = "AddItemBody" };
    private readonly Grid _detail = new() { Name = "AddItemDetail" };

    private ItemCategory _category;
    private bool? _narrow;
    private bool _updatingName;
    private bool _closed;
    private ItemTemplateDefinition? _namedFor;

    public AddItemDialog(
        IReadOnlyList<ItemTemplateDefinition> templates,
        ItemProjectProfile profile,
        string directory,
        ItemCategory initialCategory = ItemCategory.Code)
    {
        _templates = templates;
        _profile = profile;
        _directory = directory;
        _category = initialCategory;

        Title = Localizer.Get(StringKeys.AddItemTitle);
        Width = 920;
        Height = 620;

        // The floor: 640 is the narrowest the layout is built for, and the
        // height leaves the list a few rows even with the detail pane under it.
        MinWidth = 640;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        _create = new Button
        {
            Name = "AddItemAdd",
            Content = Localizer.Get(StringKeys.AddItemCreate),
            Classes = { "primary" },
            IsDefault = true
        };
        _cancel = new Button
        {
            Name = "AddItemCancel",
            Content = Localizer.Get(StringKeys.ButtonCancel),
            IsCancel = true
        };
        _create.Click += (_, _) => Accept();
        _cancel.Click += (_, _) => Finish(null);

        BuildCategories();
        BuildList();
        BuildDetail();

        _search.PlaceholderText = Localizer.Get(StringKeys.AddItemSearchPlaceholder);
        AutomationProperties.SetName(_search, _search.PlaceholderText);
        _search.GetObservable(TextBox.TextProperty).Subscribe(new TextObserver(() => Refresh()));

        // Observed rather than the TextChanged event, so a name set from code is
        // checked the same way as one typed.
        _name.GetObservable(TextBox.TextProperty).Subscribe(new TextObserver(() =>
        {
            if (!_updatingName) UpdateFeedback();
        }));

        _empty.Text = Localizer.Get(StringKeys.AddItemNoMatches);
        _empty.Margin = new Thickness(12);
        _empty.IsVisible = false;
        _empty.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        _error.Themed(TextBlock.ForegroundProperty, "ErrorForegroundBrush");
        _error.MinHeight = 18;

        AddStyles();
        Content = BuildLayout();

        // Narrow or wide is decided from the width the window really has.
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        ApplyLayout(Width);

        // Enter adds from anywhere, including the list and the search box,
        // which would otherwise swallow it; seen on the way down so a control
        // that handles Enter itself cannot hide it from the dialog.
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        SelectCategory(initialCategory);
        Opened += (_, _) =>
        {
            _name.Focus();
            SelectNameStem();
        };
    }

    /// <summary>The category the dialog is on, for the owner to remember.</summary>
    public ItemCategory Category => _category;

    /// <summary>
    /// The colours of the rows' text. Set by style rather than on each
    /// control, because a value set on the control would also beat the style
    /// that turns the text light on the selection: the selected row's text
    /// stayed dark on the blue one.
    /// </summary>
    private void AddStyles()
    {
        static Setter Brush(string key) =>
            new(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key));

        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Descendant().OfType<TextBlock>().Class("item-description"))
        {
            Setters = { Brush("DescriptionForegroundBrush") }
        });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Descendant().OfType<TextBlock>().Class("item-warning"))
        {
            Setters = { Brush("WarningForegroundBrush") }
        });

        // The selection, when the list or the tree has the focus: the selection's own
        // text colour, the one the rest of the IDE's lists use on it.
        Styles.Add(new Style(x => x.OfType<ListBox>().Class(":focus-within")
            .Descendant().OfType<ListBoxItem>().Class(":selected").Descendant().OfType<TextBlock>())
        {
            Setters = { Brush("ListActiveSelectionForegroundBrush") }
        });
        Styles.Add(new Style(x => x.OfType<TreeView>().Class(":focus-within")
            .Descendant().OfType<TreeViewItem>().Class(":selected").Descendant().OfType<TextBlock>())
        {
            Setters = { Brush("ListActiveSelectionForegroundBrush") }
        });
    }

    // ---- construction -------------------------------------------------------

    private void BuildCategories()
    {
        AutomationProperties.SetName(_categories, Localizer.Get(StringKeys.AddItemCategories));

        foreach (var category in ItemTemplateCatalog.Categories)
        {
            if (!_templates.Any(t => t.Category == category)) continue;

            var title = Localizer.Get(ItemTemplates.CategoryKey(category));

            // An icon in front of each name, so the column the tree reserves for
            // its expanders (these have nothing to expand) is not empty indent.
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Spacing.Normal,
                Children =
                {
                    new IconView { Kind = IconFor(category), Classes = { "rowIcon" }, [RowIcon.FollowsSelectionProperty] = true, IconSize = 16, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            var item = new TreeViewItem { Header = header, Tag = category };
            AutomationProperties.SetName(item, title);
            _categories.Items.Add(item);
        }

        _categories.SelectionChanged += (_, _) =>
        {
            if (_categories.SelectedItem is not TreeViewItem { Tag: ItemCategory category }) return;

            _category = category;
            Refresh();
        };
    }

    private static IconKind IconFor(ItemCategory category) => category switch
    {
        ItemCategory.Code => IconKind.VisualBasicFile,
        ItemCategory.WindowsForms => IconKind.FormItem,
        ItemCategory.Avalonia => IconKind.Window,
        ItemCategory.Web => IconKind.WebFile,
        ItemCategory.Razor => IconKind.RazorFile,
        ItemCategory.Data => IconKind.XmlFile,
        ItemCategory.Testing => IconKind.TestItem,
        _ => IconKind.TextFile
    };

    private void BuildList()
    {
        AutomationProperties.SetName(_list, Localizer.Get(StringKeys.AddItemTemplates));
        _list.SelectionChanged += (_, _) => OnTemplateChanged();
        _list.DoubleTapped += (_, e) =>
        {
            // Only a double click on a row adds; one on the scroll bar or the blank below it does not.
            if (e.Source is Visual source
                && (source is ListBoxItem || source.FindAncestorOfType<ListBoxItem>() is not null))
                Accept();
        };
    }

    private void BuildDetail()
    {
        _description.Themed(TextBlock.ForegroundProperty, "EditorForegroundBrush");
        _misfit.Themed(TextBlock.ForegroundProperty, "WarningForegroundBrush");
        _misfit.IsVisible = false;
        _files.FontSize = 11;
        _files.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        _preview.IsReadOnly = true;
        _preview.AcceptsReturn = true;
        _preview.TextWrapping = TextWrapping.NoWrap;
        _preview.FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace");
        _preview.FontSize = 12;
        _preview.MinHeight = 60;
        ScrollViewer.SetHorizontalScrollBarVisibility(_preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        AutomationProperties.SetName(_preview, Localizer.Get(StringKeys.AddItemPreviewHeader));

        _detail.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _detail.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _detail.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _detail.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _detail.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        var descriptionStack = new StackPanel
        {
            Spacing = Spacing.Tight,
            Children = { Header(StringKeys.AddItemDescriptionHeader), _description, _misfit, _files }
        };

        Grid.SetRow(descriptionStack, 0);
        _detail.Children.Add(descriptionStack);

        var previewHeader = Header(StringKeys.AddItemPreviewHeader);
        previewHeader.Margin = new Thickness(0, 8, 0, 4);
        Grid.SetRow(previewHeader, 3);
        _detail.Children.Add(previewHeader);

        Grid.SetRow(_preview, 4);
        _detail.Children.Add(_preview);
    }

    private static TextBlock Header(string key)
    {
        var header = new TextBlock { Text = Localizer.Get(key), FontWeight = FontWeight.SemiBold };
        return header;
    }

    private Control BuildLayout()
    {
        // Columns: categories | templates | detail. In narrow mode the detail
        // moves to a second row under the templates and its column collapses.
        _body.ColumnDefinitions.Add(new ColumnDefinition(CategoryWidth, GridUnitType.Pixel));
        _body.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 220 });
        _body.ColumnDefinitions.Add(new ColumnDefinition(DetailWidth, GridUnitType.Pixel));
        _body.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star) { MinHeight = 120 });
        _body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var categoryBorder = new Border
        {
            BorderThickness = new Thickness(1),
            Child = _categories,
            Margin = new Thickness(0, 0, 8, 0)
        }.Themed(Border.BorderBrushProperty, "PanelBorderBrush");

        var listPanel = new Grid
        {
            Children = { _list, _empty }
        };
        var listBorder = new Border
        {
            BorderThickness = new Thickness(1),
            Child = listPanel
        }.Themed(Border.BorderBrushProperty, "PanelBorderBrush");

        _detail.Margin = new Thickness(8, 0, 0, 0);

        Grid.SetColumn(categoryBorder, 0);
        Grid.SetRowSpan(categoryBorder, 2);
        Grid.SetColumn(listBorder, 1);
        Grid.SetColumn(_detail, 2);
        _body.Children.Add(categoryBorder);
        _body.Children.Add(listBorder);
        _body.Children.Add(_detail);

        var nameRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(1, GridUnitType.Star) },
            Children =
            {
                new TextBlock
                {
                    Text = Localizer.Get(StringKeys.AddItemName),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                },
                _name
            }
        };
        Grid.SetColumn(_name, 1);

        var layout = new Grid
        {
            Margin = Spacing.DialogPadding,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        _search.Margin = new Thickness(0, 0, 0, 8);
        nameRow.Margin = new Thickness(0, 12, 0, 0);
        _error.Margin = new Thickness(0, 4, 0, 4);
        var buttons = DialogButtons.Row(_create, _cancel);
        buttons.Margin = new Thickness(0, 4, 0, 0);

        Grid.SetRow(_body, 1);
        Grid.SetRow(nameRow, 2);
        Grid.SetRow(_error, 3);
        Grid.SetRow(buttons, 4);

        layout.Children.Add(_search);
        layout.Children.Add(_body);
        layout.Children.Add(nameRow);
        layout.Children.Add(_error);
        layout.Children.Add(buttons);

        return layout;
    }

    /// <summary>
    /// Wide: the detail is a column to the right of the list. Narrow: a row
    /// under it, across the list's column, with a height of its own so the list
    /// keeps the room it needs.
    /// </summary>
    private void ApplyLayout(double width)
    {
        var narrow = width < NarrowBelow;
        if (narrow == _narrow) return;

        _narrow = narrow;

        _body.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(DetailWidth);
        _body.RowDefinitions[1].Height = narrow ? new GridLength(170) : new GridLength(0);

        Grid.SetColumn(_detail, narrow ? 1 : 2);
        Grid.SetRow(_detail, narrow ? 1 : 0);
        _detail.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(8, 0, 0, 0);
    }

    // ---- state --------------------------------------------------------------

    private void SelectCategory(ItemCategory category)
    {
        _category = category;

        var node = _categories.Items.OfType<TreeViewItem>().FirstOrDefault(i => i.Tag is ItemCategory c && c == category)
            ?? _categories.Items.OfType<TreeViewItem>().FirstOrDefault();

        if (node is { Tag: ItemCategory chosen }) _category = chosen;

        _categories.SelectedItem = node;
        Refresh();
    }

    private ItemTemplateDefinition? Chosen =>
        _list.SelectedItem is ListBoxItem { Tag: ItemTemplateDefinition template } ? template : null;

    /// <summary>The templates the list shows now: the category's, or every match while searching.</summary>
    private IEnumerable<ItemTemplateDefinition> Visible()
    {
        var query = _search.Text ?? "";

        return string.IsNullOrWhiteSpace(query)
            ? _templates.Where(t => t.Category == _category)
            : _templates.Where(t => ItemTemplates.Matches(t, query));
    }

    private void Refresh()
    {
        var previous = Chosen;
        var shown = Visible().ToList();

        _list.Items.Clear();
        foreach (var template in shown) _list.Items.Add(BuildRow(template));

        _empty.IsVisible = shown.Count == 0;

        var keep = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is ItemTemplateDefinition t && t == previous);
        _list.SelectedItem = keep ?? _list.Items.OfType<ListBoxItem>().FirstOrDefault();

        if (shown.Count == 0) OnTemplateChanged();
    }

    private ListBoxItem BuildRow(ItemTemplateDefinition template)
    {
        var misfit = ItemTemplates.Misfit(template, _profile);

        var icon = new IconView
        {
            Kind = Enum.TryParse<IconKind>(template.Icon, out var kind) ? kind : IconKind.TextFile,
            Classes = { "rowIcon" },
            [RowIcon.FollowsSelectionProperty] = true,
            IconSize = 24,
            VerticalAlignment = VerticalAlignment.Center
        };

        var text = new StackPanel { Spacing = Spacing.Hairline, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = template.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });

        var description = new TextBlock
        {
            Text = template.Description,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "item-description" }
        };
        text.Children.Add(description);

        if (misfit is not null)
        {
            text.Children.Add(new TextBlock
            {
                Name = "AddItemRowMisfit",
                Text = Localizer.Get(StringKeys.AddItemNotForProject),
                FontSize = 11,
                Classes = { "item-warning" }
            });
        }

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(32, GridUnitType.Pixel), new ColumnDefinition(1, GridUnitType.Star) },
            Margin = new Thickness(0, 2),
            Children = { icon, text }
        };
        Grid.SetColumn(text, 1);

        var item = new ListBoxItem { Content = row, Tag = template };
        AutomationProperties.SetName(item, template.Name);
        return item;
    }

    private void OnTemplateChanged()
    {
        var template = Chosen;

        _description.Text = template?.Description ?? "";

        var misfit = template is null ? null : ItemTemplates.Misfit(template, _profile);
        _misfit.IsVisible = misfit is not null;
        _misfit.Text = misfit is { } missing
            ? Localizer.Get(StringKeys.AddItemMisfitNote, Describe(missing))
            : "";

        if (template is not null && template != _namedFor)
        {
            // Choosing a template suggests its file name, as Visual Studio does
            // with Class1.vb, so the name is never left describing another
            // template; counted up when that one is already in the folder.
            _updatingName = true;
            _name.Text = ItemTemplates.SuggestName(template, FileExistsInFolder);
            _updatingName = false;
            _namedFor = template;
            SelectNameStem();
        }

        UpdateFeedback();
    }

    /// <summary>
    /// Selects the name without its extension, as Visual Studio does, so typing
    /// replaces "Class1" and keeps ".vb". The whole name, digit included.
    /// </summary>
    private void SelectNameStem()
    {
        var text = _name.Text ?? "";
        var extension = Chosen?.Extension ?? "";
        var end = extension.Length > 0 && text.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? text.Length - extension.Length
            : text.Length;

        _name.SelectionStart = 0;
        _name.SelectionEnd = end;
    }

    private bool FileExistsInFolder(string fileName) =>
        ExplorerFileOperations.Validate(fileName, _directory) == ExplorerFileOperations.NameProblem.AlreadyExists;

    internal static string Describe(ItemRequirement requirement) => requirement switch
    {
        ItemRequirement.Web => Localizer.Get(StringKeys.AddItemNeedsWeb),
        ItemRequirement.WindowsForms => Localizer.Get(StringKeys.AddItemNeedsWindowsForms),
        ItemRequirement.Avalonia => Localizer.Get(StringKeys.AddItemNeedsAvalonia),
        ItemRequirement.XUnit => Localizer.Get(StringKeys.AddItemNeedsXUnit),
        ItemRequirement.NUnit => Localizer.Get(StringKeys.AddItemNeedsNUnit),
        ItemRequirement.MSTest => Localizer.Get(StringKeys.AddItemNeedsMSTest),
        _ => ""
    };

    /// <summary>
    /// What is wrong with the name now, or null: it can be a name, and no file
    /// the template would write is already in the folder. Also what the
    /// preview is generated for.
    /// </summary>
    private (string? FileName, string? Problem) Validate(ItemTemplateDefinition template)
    {
        var typed = _name.Text ?? "";
        var result = ItemTemplates.Check(template, typed, ExplorerFileOperations.IsVisualBasicReservedWord);

        switch (result.Problem)
        {
            case ItemNameProblem.Empty:
                return (null, Localizer.Get(StringKeys.AddItemNameEmpty));
            case ItemNameProblem.NotAnIdentifier:
                return (null, Localizer.Get(StringKeys.AddItemNameInvalid));
            case ItemNameProblem.InvalidFileName:
                return (null, Localizer.Get(StringKeys.AddItemNameFileInvalid, typed.Trim()));
            case ItemNameProblem.ShadowsBaseType:
                return (null, Localizer.Get(StringKeys.AddItemNameShadows, typed.Trim()));
        }

        foreach (var file in ItemTemplates.FilesFor(template, result.FileName!))
        {
            if (ExplorerFileOperations.Validate(file.FileName, _directory) != ExplorerFileOperations.NameProblem.None)
                return (result.FileName, Localizer.Get(StringKeys.AddItemNameExists, file.FileName));
        }

        return (result.FileName, null);
    }

    private void UpdateFeedback()
    {
        if (Chosen is not { } template)
        {
            _error.Text = "";
            _preview.Text = "";
            _files.Text = "";
            return;
        }

        var (fileName, problem) = Validate(template);
        _error.Text = problem ?? "";

        // The preview is of the name typed when it is one, and of the
        // suggestion otherwise, so it never goes blank under the user's hands.
        var previewName = fileName ?? template.DefaultName;
        var files = ItemTemplates.FilesFor(template, previewName);

        _files.Text = Localizer.Get(StringKeys.AddItemFilesHeader, string.Join(", ", files.Select(f => f.FileName)));
        _preview.Text = files.Count == 1
            ? files[0].Content
            : string.Join("\n", files.Select(f => $"---- {f.FileName} ----\n{f.Content}"));
        _preview.CaretIndex = 0;
    }

    // ---- accepting ----------------------------------------------------------

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || e.KeyModifiers != KeyModifiers.None) return;

        // A button has its own Enter: Cancel cancels.
        if (e.Source is Button source && source == _cancel) return;

        e.Handled = true;
        Accept();
    }

    private void Accept()
    {
        if (_closed || Chosen is not { } template) return;

        var (fileName, problem) = Validate(template);

        if (problem is not null || fileName is null)
        {
            _error.Text = problem ?? Localizer.Get(StringKeys.AddItemNameInvalid);
            return;
        }

        Finish(new AddItemChoice(template, fileName, ItemTemplates.Misfit(template, _profile)));
    }

    private void Finish(AddItemChoice? choice)
    {
        if (_closed) return;

        _closed = true;
        Close(choice);
    }

    // ---- tests --------------------------------------------------------------

    internal bool IsNarrow => _narrow == true;

    internal Control DetailForTests => _detail;

    internal Control ListForTests => _list;

    private sealed class TextObserver(Action action) : IObserver<string?>
    {
        public void OnNext(string? value) => action();

        public void OnError(Exception error) { }

        public void OnCompleted() { }
    }
}
