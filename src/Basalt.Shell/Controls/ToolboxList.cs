using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;

namespace Basalt.Shell.Controls;

/// <summary>
/// The body both toolboxes share: a filter box, a list of collapsible
/// categories, and a message when the filter leaves nothing.
///
/// The Avalonia toolbox had category headings you could not fold and the Forms
/// toolbox a flat alphabetical run of a hundred names. Visual Studio's answer
/// is a box to type into and categories that fold, so the control you want is
/// two keystrokes away instead of a scroll through everything.
///
/// The rows themselves belong to the caller: this class knows about headings,
/// folding and filtering, not about what a button looks like.
/// </summary>
public sealed class ToolboxList : UserControl
{
    private readonly TextBox _filter;
    private readonly EmptyState _noMatches;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private Func<string?, IReadOnlySet<string>, List<object>> _build = (_, _) => [];
    private Func<object, Control> _row = _ => new TextBlock();
    // One collection for the life of the list: replacing ItemsSource rebuilds
    // every row, including the heading under the pointer in the middle of its
    // own press, and loses the scroll position on every keystroke in the filter.
    private readonly ObservableCollection<object> _rows = [];

    public ToolboxList()
    {
        List = new ListBox
        {
            ItemsSource = _rows,
            FontSize = PanelMetrics.FontSize,
            ItemTemplate = new FuncDataTemplate<object>(
                (item, _) => item switch
                {
                    ToolboxHeading heading => HeadingRow(heading),
                    null => new TextBlock(),
                    _ => _row(item),
                },
                supportsRecycling: false),
        };

        _filter = new TextBox
        {
            Watermark = Localizer.Get(StringKeys.ToolboxFilterWatermark),
            FontSize = PanelMetrics.FontSize,
            Margin = new Thickness(Spacing.Tight),
        };
        ToolTip.SetTip(_filter, Localizer.Get(StringKeys.ToolboxFilterTip));
        AutomationProperties(_filter);

        _noMatches = new EmptyState(string.Empty, IconKind.Toolbox) { IsVisible = false };

        // The property rather than TextChanged: the event is raised from the
        // template's text presenter, so setting the text before the box has been
        // laid out (a test, or a restored filter) would not filter anything.
        _filter.GetObservable(TextBox.TextProperty).Subscribe(new ActionObserver(() => Refresh()));
        _filter.KeyDown += OnFilterKey;
        // Tunnelled: ListBox consumes the arrow keys for its own navigation
        // before a bubbling handler sees them, and Left/Right fold headings.
        List.AddHandler(KeyDownEvent, OnListKey, RoutingStrategies.Tunnel);
        // A heading has already folded on the first press of the double-click;
        // folding again here would put it back. Only entries are activated.
        List.DoubleTapped += (_, _) =>
        {
            if (List.SelectedItem is { } item and not ToolboxHeading) ItemActivated?.Invoke(this, item);
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { _filter, List, _noMatches },
        };
        Grid.SetRow(List, 1);
        Grid.SetRow(_noMatches, 1);

        Content = layout;
    }

    /// <summary>The list, for the drag handlers and the tests that click on it.</summary>
    public ListBox List { get; }

    /// <summary>The filter box.</summary>
    public TextBox Filter => _filter;

    /// <summary>An entry was chosen with Enter or a double-click.</summary>
    public event EventHandler<object>? ItemActivated;

    /// <summary>What is typed in the filter.</summary>
    public string FilterText
    {
        get => _filter.Text ?? string.Empty;
        set => _filter.Text = value;
    }

    /// <summary>Whether the "no matches" message is showing.</summary>
    public bool ShowsNoMatches => _noMatches.IsVisible;

    /// <summary>The message of the empty state, for the tests.</summary>
    public string NoMatchesMessage => _noMatches.Message;

    /// <summary>Shows <paramref name="items"/>, grouped under their categories.</summary>
    public void Show<T>(
        IReadOnlyList<T> items,
        Func<T, string> category,
        Func<T, IEnumerable<string>> searchText,
        Func<T, Control> row) where T : notnull
    {
        _build = (filter, collapsed) => ToolboxListing.Build(items, category, searchText, filter, collapsed);
        _row = item => row((T)item);
        Refresh();
    }

    /// <summary>Whether a category is folded.</summary>
    public bool IsCollapsed(string category) => _collapsed.Contains(category);

    /// <summary>Folds or unfolds a category.</summary>
    public void Toggle(string category)
    {
        if (!_collapsed.Remove(category)) _collapsed.Add(category);

        Refresh(keepSelection: category);
    }

    private void Refresh(string? keepSelection = null)
    {
        var rows = _build(FilterText, _collapsed);

        object? selected = keepSelection is null
            ? List.SelectedItem
            : rows.OfType<ToolboxHeading>().FirstOrDefault(h => h.Name == keepSelection);

        Sync(rows);

        // A refresh rebuilds the rows, so the selection would be lost on every
        // keystroke in the filter. Headings are records: equal by value, so a
        // heading is found again even though its Collapsed flag changed.
        if (selected is ToolboxHeading heading)
            selected = rows.OfType<ToolboxHeading>().FirstOrDefault(h => h.Name == heading.Name);

        if (selected is not null && rows.Contains(selected)) List.SelectedItem = selected;

        var empty = rows.Count == 0 && FilterText.Trim().Length > 0;
        _noMatches.IsVisible = empty;
        List.IsVisible = !empty;

        if (empty)
            _noMatches.Message = string.Format(Localizer.Get(StringKeys.ToolboxNoMatches), FilterText.Trim());
    }

    /// <summary>Makes the shared collection equal to <paramref name="rows"/> with the fewest changes.</summary>
    private void Sync(List<object> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (i < _rows.Count && Equals(_rows[i], rows[i])) continue;

            // The wanted row further down means the ones before it went away
            // (a filter); otherwise it is new, or a heading whose flag changed.
            var found = -1;
            for (var j = i + 1; j < _rows.Count && found < 0; j++)
                if (Equals(_rows[j], rows[i])) found = j;

            if (found >= 0)
                for (var k = found - 1; k >= i; k--) _rows.RemoveAt(k);
            else
                _rows.Insert(i, rows[i]);
        }

        while (_rows.Count > rows.Count) _rows.RemoveAt(_rows.Count - 1);
    }

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when List.SelectedItem is ToolboxHeading enterHeading:
                // Folding is what a heading does; inserting the word "Layout"
                // into the form is not.
                Toggle(enterHeading.Name);
                e.Handled = true;
                break;

            case Key.Left or Key.Right when List.SelectedItem is ToolboxHeading heading:
                if (IsCollapsed(heading.Name) == (e.Key == Key.Right)) Toggle(heading.Name);
                e.Handled = true;
                break;

            case Key.Left when List.SelectedItem is { } item && CategoryHeadingBefore(item) is { } owner:
                // From inside a category, Left climbs to its heading, as in a tree.
                List.SelectedItem = owner;
                e.Handled = true;
                break;

            case Key.Enter when List.SelectedItem is { } chosen:
                ItemActivated?.Invoke(this, chosen);
                e.Handled = true;
                break;
        }
    }

    private ToolboxHeading? CategoryHeadingBefore(object item)
    {
        var rows = _rows;
        var index = rows.IndexOf(item);

        for (var i = index - 1; i >= 0; i--)
            if (rows[i] is ToolboxHeading heading) return heading;

        return null;
    }

    private void OnFilterKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && FilterText.Length > 0)
        {
            FilterText = string.Empty;
            e.Handled = true;
        }
        else if (e.Key == Key.Down && List.ItemCount > 0)
        {
            // From the box straight into the list: filtering is pointless if the
            // result then has to be reached with the mouse.
            List.Focus();
            if (List.SelectedItem is null) List.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && FirstItem() is { } first)
        {
            // Typing "check" and pressing Enter adds the first match, which
            // is what a filter box that is also a command line should do.
            ItemActivated?.Invoke(this, first);
            e.Handled = true;
        }
    }

    private object? FirstItem() =>
        _rows.FirstOrDefault(row => row is not ToolboxHeading);

    private Control HeadingRow(ToolboxHeading heading)
    {
        var chevron = new IconView
        {
            Kind = heading.Collapsed ? IconKind.ChevronRight : IconKind.ChevronDown,
            IconSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        }.Themed(IconView.ForegroundProperty, "ChromeSecondaryForegroundBrush");

        var name = new TextBlock
        {
            Text = heading.Name,
            FontSize = PanelMetrics.FontSize,
            // Bold, not SemiBold: the first SemiBold run drawn in a process came out
            // in the regular face (measured), so the first heading looked unbolded.
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        }.Themed(TextBlock.ForegroundProperty, "ChromeForegroundBrush");

        var count = new TextBlock
        {
            Text = heading.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontSize = PanelMetrics.SecondaryFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        }.Themed(TextBlock.ForegroundProperty, "ChromeSecondaryForegroundBrush");

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Small,
            Background = Brushes.Transparent, // hit-testable across the whole row
            Children = { chevron, name, count },
        };

        // The first press of a click or double-click folds; the second press
        // and the DoubleTapped that follows do not. The list is changed after
        // the press has finished: rebuilding the row under the pointer in the
        // middle of its own event loses the selection and the capture.
        row.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != 1 || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            var name = heading.Name;
            Dispatcher.UIThread.Post(() => Toggle(name));
        };

        return row;
    }

    private static void AutomationProperties(Control control) =>
        Avalonia.Automation.AutomationProperties.SetName(control, Localizer.Get(StringKeys.ToolboxFilterTip));
}

file sealed class ActionObserver(Action action) : IObserver<string?>
{
    public void OnNext(string? value) => action();
    public void OnError(Exception error) { }
    public void OnCompleted() { }
}
