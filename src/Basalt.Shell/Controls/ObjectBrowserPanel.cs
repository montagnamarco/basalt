using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Workspace.ObjectBrowser;

namespace Basalt.Shell.Controls;

/// <summary>
/// The Object Browser: the types and members of the solution and of the
/// libraries it references, with a description of what is selected.
///
/// Laid out as Visual Basic 6's was, because that arrangement is the point:
/// a library selector and a search box on top, types on the left, members on
/// the right, and the declaration, its container and its summary below.
///
/// Every question goes through an <see cref="ObjectBrowserLoader"/>, which runs
/// it on a pool thread and drops the answer when a newer question replaced it,
/// so arrowing down a list of types never shows the members of one it passed.
/// What this class does is apply the answers, on the thread that awaited.
/// </summary>
public sealed class ObjectBrowserPanel : UserControl
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(1500);

    private readonly ObjectBrowserLoader _loader;
    private readonly ComboBox _libraryBox;
    private readonly TextBox _search;
    private readonly ToggleButton _inherited;
    private readonly ListBox _typeList;
    private readonly ListBox _memberList;
    private readonly ObservableCollection<object> _typeRows = [];
    private readonly ObservableCollection<BrowserMemberRow> _memberRows = [];
    private readonly TextBlock _signature;
    private readonly TextBlock _details;
    private readonly TextBlock _summary;
    private readonly EmptyState _empty;

    private IReadOnlyList<BrowserLibrary> _libraries = [];
    private IReadOnlyList<string> _namespaces = [];
    private IReadOnlyList<BrowserSymbol> _searchResults = [];
    private IReadOnlyList<BrowserSymbol> _allMembers = [];
    private BrowserLibrary? _library;
    private BrowserSymbol? _type;
    private string _query = "";
    private bool _applyingLibraries;
    private CancellationTokenSource? _searchDebounce;
    private CancellationTokenSource? _refreshDebounce;
    private bool _settingSearchText;
    private bool _stale;
    private bool _restoring;
    private TextBlock _typesHeader = null!;

    public ObjectBrowserPanel(ObjectBrowserLoader loader)
    {
        _loader = loader;

        _libraryBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = PanelMetrics.FontSize,
            MinHeight = Spacing.EditorHeight,
            Padding = new Thickness(Spacing.Tight, 2)
        };
        ToolTip.SetTip(_libraryBox, Localizer.Get(StringKeys.ObjectBrowserLibraryTip));
        Name(_libraryBox, StringKeys.ObjectBrowserLibraryTip);
        _libraryBox.SelectionChanged += (_, _) =>
        {
            if (!_applyingLibraries && _libraryBox.SelectedIndex is >= 0 and var index && index < _libraries.Count)
                Fire(() => SelectLibraryAsync(_libraries[index]));
        };

        var refresh = PanelToolbar.IconButton(
            IconKind.Refresh, Localizer.Get(StringKeys.ObjectBrowserRefresh), () => Fire(RefreshAsync));

        _search = PanelToolbar.Filter(Localizer.Get(StringKeys.ObjectBrowserSearchPlaceholder));
        Name(_search, StringKeys.ObjectBrowserSearchPlaceholder);
        _search.GetObservable(TextBox.TextProperty).Subscribe(new TextObserver(OnSearchTextChanged));

        _inherited = PanelToolbar.TextToggle(
            Localizer.Get(StringKeys.ObjectBrowserInherited),
            Localizer.Get(StringKeys.ObjectBrowserInheritedTip));
        _inherited.IsCheckedChanged += (_, _) => Fire(ReloadMembersAsync);

        _typeList = BuildList(_typeRows, StringKeys.ObjectBrowserTypes);
        _memberList = BuildList(_memberRows, StringKeys.ObjectBrowserMembers);

        _typeList.SelectionChanged += (_, _) => { if (!_restoring) Fire(() => OnTypeSelectedAsync(_typeList.SelectedItem)); };
        _memberList.SelectionChanged += (_, _) => { if (!_restoring) Fire(() => OnMemberSelectedAsync(_memberList.SelectedItem)); };
        _typeList.AddHandler(KeyDownEvent, OnTypeListKey, RoutingStrategies.Tunnel);
        _memberList.AddHandler(KeyDownEvent, OnMemberListKey, RoutingStrategies.Tunnel);
        _typeList.DoubleTapped += (_, _) => Fire(() => ActivateTypeRowAsync(_typeList.SelectedItem));
        _memberList.DoubleTapped += (_, _) => Fire(() => ActivateAsync(SelectedSymbol(_memberList.SelectedItem)));

        _typeList.ContextMenu = BuildMenu(() => SelectedSymbol(_typeList.SelectedItem));
        _memberList.ContextMenu = BuildMenu(() => SelectedSymbol(_memberList.SelectedItem));

        _signature = new TextBlock
        {
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = PanelMetrics.FontSize,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        _details = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            TextWrapping = TextWrapping.Wrap
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        _summary = new TextBlock
        {
            FontSize = PanelMetrics.FontSize,
            TextWrapping = TextWrapping.Wrap
        };

        var description = new ScrollViewer
        {
            Padding = new Thickness(Spacing.Normal, Spacing.Tight),
            Content = new StackPanel
            {
                Spacing = Spacing.Tight,
                Children = { _signature, _details, _summary }
            }
        };

        _empty = new EmptyState(Localizer.Get(StringKeys.ObjectBrowserEmpty), IconKind.Outline);

        Content = BuildLayout(refresh, description);
        ShowDescriptionHint();
    }

    /// <summary>Raised when the user opens a symbol that is declared in the solution's own source.</summary>
    public event EventHandler<BrowserSourceTarget>? SourceRequested;

    /// <summary>Asks the host to put a message where the user will see it; used for failures a list cannot show.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>The rows of the type list, for tests.</summary>
    internal IReadOnlyList<object> TypeRows => _typeRows.ToList();

    /// <summary>The rows of the member list, for tests.</summary>
    internal IReadOnlyList<BrowserMemberRow> MemberRows => _memberRows.ToList();

    internal string SignatureText => _signature.Text ?? "";

    internal string DetailsText => _details.Text ?? "";

    internal string SummaryText => _summary.Text ?? "";

    /// <summary>Whether a source path can be opened; the tests stand in for the file system.</summary>
    internal Func<string, bool> PathExists { get; set; } = File.Exists;

    /// <summary>The heading over the type list, which says when a search was cut short.</summary>
    internal string TypesHeaderText => _typesHeader.Text ?? "";

    internal bool ShowsEmptyState => _empty.IsVisible;

    internal ListBox TypeList => _typeList;

    internal ListBox MemberList => _memberList;

    internal ComboBox LibraryBox => _libraryBox;

    internal TextBox SearchBox => _search;

    internal ToggleButton InheritedToggle => _inherited;

    /// <summary>The libraries offered, in the order of the selector.</summary>
    internal IReadOnlyList<BrowserLibrary> Libraries => _libraries;

    private Grid BuildLayout(Button refresh, Control description)
    {
        var libraryRow = PanelToolbar.Row(_libraryBox, refresh);
        var searchRow = PanelToolbar.Row(_search, _inherited);

        var lists = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            Children =
            {
                WithHeader(StringKeys.ObjectBrowserTypes, _typeList, out _typesHeader),
                Splitter(vertical: true),
                WithHeader(StringKeys.ObjectBrowserMembers, _memberList, out _)
            }
        };
        Grid.SetColumn(lists.Children[0], 0);
        Grid.SetColumn(lists.Children[1], 1);
        Grid.SetColumn(lists.Children[2], 2);

        var body = new Grid { Children = { lists, _empty } };
        _empty.IsVisible = true;
        lists.IsVisible = false;
        _empty.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty) lists.IsVisible = !_empty.IsVisible;
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,110"),
            Children = { libraryRow, searchRow, body, Splitter(vertical: false), description }
        };

        Grid.SetRow(libraryRow, 0);
        Grid.SetRow(searchRow, 1);
        Grid.SetRow(body, 2);
        Grid.SetRow(layout.Children[3], 3);
        Grid.SetRow(description, 4);

        return layout;
    }

    private static Control WithHeader(string headerKey, ListBox list, out TextBlock header)
    {
        header = new TextBlock
        {
            Text = Localizer.Get(headerKey),
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight)
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        var panel = new DockPanel();
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(list);
        return panel;
    }

    private static GridSplitter Splitter(bool vertical) => new GridSplitter
    {
        ResizeDirection = vertical ? GridResizeDirection.Columns : GridResizeDirection.Rows,
        Width = vertical ? Spacing.Tight : double.NaN,
        Height = vertical ? double.NaN : Spacing.Tight,
        HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
        VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Center
    }.Themed(GridSplitter.BackgroundProperty, "PanelBorderBrush");

    private static ListBox BuildList(System.Collections.IEnumerable rows, string nameKey)
    {
        var list = new ListBox
        {
            ItemsSource = rows,
            FontSize = PanelMetrics.FontSize,
            // Left as the default virtualising panel: System.Private.CoreLib
            // alone is close to three thousand types, so a list that builds a
            // row for each would be what makes opening the browser slow.
            ItemTemplate = new FuncDataTemplate<object>((row, _) => ObjectBrowserRows.Build(row), supportsRecycling: false)
        };

        Name(list, nameKey);
        return list;
    }

    private static void Name(Control control, string key) =>
        Avalonia.Automation.AutomationProperties.SetName(control, Localizer.Get(key));

    private ContextMenu BuildMenu(Func<BrowserSymbol?> selected) => ContextMenus.Build(
    [
        new(Localizer.Get(StringKeys.ObjectBrowserCopySignature), () => Fire(() => CopySignatureAsync(selected())))
        {
            IsAvailable = () => selected() is not null,
            Icon = IconKind.Copy
        },
        new(Localizer.Get(StringKeys.ObjectBrowserGoToSource), () => Fire(() => ActivateAsync(selected())))
        {
            // A declaration in generated code maps to a path nobody can open.
            IsAvailable = () => selected()?.Source is { } source && PathExists(source.FilePath),
            Icon = IconKind.GoToDefinition
        }
    ]);

    private static BrowserSymbol? SelectedSymbol(object? row) => row switch
    {
        BrowserTypeRow type => type.Symbol,
        BrowserMemberRow member => member.Symbol,
        _ => null
    };

    // ---- Loading ----------------------------------------------------------

    /// <summary>
    /// Reads the solution's libraries again. The library, the open namespaces,
    /// the search, the selected type and member and the scroll positions are
    /// kept: the lists are rebuilt aside and swapped in, so a refresh that
    /// finds nothing changed is not seen.
    /// </summary>
    public async Task RefreshAsync()
    {
        _refreshDebounce?.Cancel();
        _stale = false;

        // A click on a symbol in flight is not about the old libraries: it
        // resolves against the current compilation by itself.
        _loader.CancelAllExcept("navigate");

        var libraries = await _loader.LatestAsync("libraries", (service, ct) => service.GetLibrariesAsync(ct));
        if (libraries is null) return;

        var previous = _library;
        var keep = previous is null
            ? null
            : libraries.FirstOrDefault(l => l.Kind == previous.Kind && l.Name == previous.Name);

        _libraries = libraries;

        _applyingLibraries = true;
        _libraryBox.ItemsSource = libraries.Select(LabelOf).ToList();
        _libraryBox.SelectedIndex = keep is null ? 0 : libraries.ToList().IndexOf(keep);
        _applyingLibraries = false;

        if (keep is null || previous is null)
        {
            // Nothing worth keeping: a first load, or the library is gone.
            _allMembers = [];
            _searchResults = [];
            await SelectLibraryAsync(libraries.FirstOrDefault());
            return;
        }

        await ReloadInPlaceAsync(previous, keep);
    }

    private async Task ReloadInPlaceAsync(BrowserLibrary previous, BrowserLibrary library)
    {
        var query = _query;
        var inherited = _inherited.IsChecked == true;
        var openNamespaces = _typeRows.OfType<BrowserNamespaceRow>().Where(r => r.IsExpanded).Select(r => r.Name).ToHashSet();
        var wantedType = KeyOf(_type);
        var wantedMember = _memberList.SelectedItem is BrowserMemberRow m ? (m.Symbol.Name, m.Symbol.Signature) : default((string, string)?);
        var typeScroll = ScrollerOf(_typeList)?.Offset;
        var memberScroll = ScrollerOf(_memberList)?.Offset;

        // A refresh that gives up leaves the lists as they were; they are
        // read again soon, so the panel is never left on the old compilation.
        void Abandon()
        {
            _stale = true;
            ScheduleRefresh();
        }

        var rows = new List<object>();
        IReadOnlyList<string> namespaces = _namespaces;
        BrowserSearchResult? search = null;

        if (query.Length > 0)
        {
            search = await _loader.LatestAsync("search", (service, ct) => service.SearchAsync(library, query, ct));
            if (search is null) { Abandon(); return; }

            rows.AddRange(search.Types.Select(type => new BrowserTypeRow(type, UnderNamespace: false)));
        }
        else
        {
            var names = await _loader.LatestAsync("namespaces", (service, ct) => service.GetNamespacesAsync(library, ct));
            if (names is null) { Abandon(); return; }

            namespaces = names;

            foreach (var name in names)
            {
                var expanded = openNamespaces.Contains(name);
                rows.Add(new BrowserNamespaceRow(name, expanded));
                if (!expanded) continue;

                var types = await _loader.LatestAsync("types:" + name, (service, ct) => service.GetTypesAsync(library, name, ct));
                if (types is null) { Abandon(); return; }

                rows.AddRange(types.Select(type => new BrowserTypeRow(type, UnderNamespace: true)));
            }
        }

        var newType = wantedType is { } wanted
            ? rows.OfType<BrowserTypeRow>().FirstOrDefault(r => r.Symbol.Namespace == wanted.Item1 && r.Symbol.Name == wanted.Item2)?.Symbol
            : null;

        IReadOnlyList<BrowserSymbol> members = [];
        if (newType is not null)
        {
            var loaded = await _loader.LatestAsync("members", (service, ct) => service.GetMembersAsync(newType, inherited, ct));
            if (loaded is null) { Abandon(); return; }

            members = loaded;
        }

        if (AfterFetchForTests is { } hook) await hook();

        // The user moved on while this was being read: another library, a new
        // search, another type. What was read answers an older question, and
        // swapping it in would put the old rows back over the new ones.
        if (_library != previous || _query != query || KeyOf(_type) != wantedType)
        {
            Abandon();
            return;
        }

        _restoring = true;
        try
        {
            _library = library;
            _namespaces = namespaces;
            _searchResults = search?.Types ?? [];

            _typeRows.Clear();
            foreach (var row in rows) _typeRows.Add(row);

            _typesHeader.Text = search is { IsTruncated: true }
                ? string.Format(Localizer.Get(StringKeys.ObjectBrowserTypesCapped), search.Types.Count, search.Total)
                : Localizer.Get(StringKeys.ObjectBrowserTypes);

            if (rows.Count == 0)
                ShowEmpty(query.Length > 0
                    ? string.Format(Localizer.Get(StringKeys.ObjectBrowserNoMatches), query)
                    : Localizer.Get(StringKeys.ObjectBrowserEmpty));
            else
                HideEmpty();

            _type = newType;
            _allMembers = members;
            _memberRows.Clear();

            if (newType is not null)
            {
                _typeList.SelectedItem = rows.OfType<BrowserTypeRow>().First(r => r.Symbol == newType);
                ApplyMemberFilter();

                if (wantedMember is { } member)
                    _memberList.SelectedItem = _memberRows.FirstOrDefault(
                        r => r.Symbol.Name == member.Item1 && r.Symbol.Signature == member.Item2);
            }
            else
            {
                ShowDescriptionHint();
            }
        }
        finally
        {
            _restoring = false;
        }

        // The description is of the new symbol: its summary may have been edited.
        if (_memberList.SelectedItem is BrowserMemberRow selected)
            await DescribeAsync(selected.Symbol);
        else if (newType is not null)
            await DescribeAsync(newType);

        // Offsets only mean something once the new rows have been measured.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (typeScroll is { } type && ScrollerOf(_typeList) is { } typeScroller) typeScroller.Offset = type;
            if (memberScroll is { } member && ScrollerOf(_memberList) is { } memberScroller) memberScroller.Offset = member;
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private static (string, string)? KeyOf(BrowserSymbol? type) =>
        type is null ? null : (type.Namespace, type.Name);

    /// <summary>Runs between reading and swapping in a refresh, so a test can move the user on at that moment.</summary>
    internal Func<Task>? AfterFetchForTests { get; set; }

    private static ScrollViewer? ScrollerOf(ListBox list) =>
        list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>A browser that was out of sight while the solution changed reads again as it comes back.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_stale) Fire(RefreshAsync);
    }

    /// <summary>
    /// Asks for the libraries to be read again soon: the solution changed, or
    /// the text of a file did. Debounced, since text changes arrive with every
    /// keystroke, and postponed while the browser is not on screen — it reads
    /// again when it is shown.
    /// </summary>
    public void ScheduleRefresh()
    {
        if (_libraries.Count == 0) return;

        // Inactive tabs are taken out of the visual tree by the dock.
        if (!IsEffectivelyVisible || TopLevel.GetTopLevel(this) is null)
        {
            _stale = true;
            return;
        }

        _refreshDebounce?.Cancel();
        _refreshDebounce = new CancellationTokenSource();
        var token = _refreshDebounce.Token;

        Fire(async () =>
        {
            try
            {
                await Task.Delay(RefreshDelay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RefreshAsync();
        });
    }

    /// <summary>True when the panel was hidden while the solution changed and has not read it since.</summary>
    internal bool IsStale => _stale;

    /// <summary>Reads again if what is shown predates a change.</summary>
    public Task RefreshIfStaleAsync() => _stale ? RefreshAsync() : Task.CompletedTask;

    private string LabelOf(BrowserLibrary library) =>
        library.Kind == BrowserLibraryKind.All ? Localizer.Get(StringKeys.ObjectBrowserAllLibraries) : library.Name;

    /// <summary>Chooses a library: its namespaces are listed, or its search results if a search is typed.</summary>
    public async Task SelectLibraryAsync(BrowserLibrary? library)
    {
        // What was in flight belonged to the previous library — except the
        // list of libraries, which a solution reload may be filling in.
        _loader.CancelAllExcept("libraries", "navigate");

        _library = library;
        _namespaces = [];
        _searchResults = [];
        ClearMembers();
        _typeRows.Clear();

        if (library is null)
        {
            ShowEmpty(Localizer.Get(StringKeys.ObjectBrowserEmpty));
            return;
        }

        if (_query.Length > 0)
        {
            await SearchAsync(library, _query);
            return;
        }

        var namespaces = await _loader.LatestAsync("namespaces", (service, ct) => service.GetNamespacesAsync(library, ct));
        if (namespaces is null || library != _library) return;

        _namespaces = namespaces;
        ShowNamespaces();
    }

    private void ShowNamespaces()
    {
        _typeRows.Clear();

        foreach (var name in _namespaces) _typeRows.Add(new BrowserNamespaceRow(name));

        if (_namespaces.Count == 0)
            ShowEmpty(Localizer.Get(StringKeys.ObjectBrowserEmpty));
        else
            HideEmpty();
    }

    /// <summary>Opens or closes a namespace; opening reads its types, and only then.</summary>
    public async Task ToggleNamespaceAsync(BrowserNamespaceRow row)
    {
        var library = _library;
        var index = _typeRows.IndexOf(row);
        if (library is null || index < 0) return;

        if (row.IsExpanded)
        {
            // Everything indented under it, up to the next namespace.
            while (index + 1 < _typeRows.Count && _typeRows[index + 1] is BrowserTypeRow)
                _typeRows.RemoveAt(index + 1);

            ReplaceKeepingSelection(index, row with { IsExpanded = false });
            return;
        }

        var types = await _loader.LatestAsync(
            "types:" + row.Name, (service, ct) => service.GetTypesAsync(library, row.Name, ct));

        // The library changed, or the row was replaced, while the types loaded.
        if (types is null || library != _library) return;

        index = _typeRows.IndexOf(row);
        if (index < 0) return;

        ReplaceKeepingSelection(index, row with { IsExpanded = true });

        foreach (var type in types.Reverse())
            _typeRows.Insert(index + 1, new BrowserTypeRow(type, UnderNamespace: true));
    }

    /// <summary>
    /// Swaps a namespace row for its folded or unfolded twin. Replacing the
    /// item makes the list drop its selection, and with it the keyboard
    /// position: Right to open and Left to close would stop after the first.
    /// </summary>
    private void ReplaceKeepingSelection(int index, BrowserNamespaceRow replacement)
    {
        var hadSelection = _typeList.SelectedIndex == index;

        _typeRows[index] = replacement;

        if (hadSelection) _typeList.SelectedIndex = index;
    }

    /// <summary>Filters by a query now, without the typing delay, and shows it in the search box.</summary>
    public async Task SetQueryAsync(string query)
    {
        _searchDebounce?.Cancel();

        if (_search.Text != query)
        {
            _settingSearchText = true;
            try
            {
                _search.Text = query;
            }
            finally
            {
                _settingSearchText = false;
            }
        }

        await ApplyQueryAsync(query);
    }

    /// <summary>Applies a query; never touches the box, which belongs to whoever is typing in it.</summary>
    private async Task ApplyQueryAsync(string query)
    {
        _query = query.Trim();

        if (_library is not { } library) return;

        if (_query.Length == 0)
        {
            _searchResults = [];
            _typesHeader.Text = Localizer.Get(StringKeys.ObjectBrowserTypes);
            await SelectLibraryAsync(library);
            return;
        }

        await SearchAsync(library, _query);
    }

    private async Task SearchAsync(BrowserLibrary library, string query)
    {
        var result = await _loader.LatestAsync("search", (service, ct) => service.SearchAsync(library, query, ct));
        if (result is null || library != _library || query != _query) return;

        _searchResults = result.Types;
        _typeRows.Clear();

        foreach (var type in result.Types) _typeRows.Add(new BrowserTypeRow(type, UnderNamespace: false));

        _typesHeader.Text = result.IsTruncated
            ? string.Format(Localizer.Get(StringKeys.ObjectBrowserTypesCapped), result.Types.Count, result.Total)
            : Localizer.Get(StringKeys.ObjectBrowserTypes);

        if (result.Types.Count == 0)
        {
            // What was being asked about the previous results is moot.
            _loader.Cancel("members");
            _loader.Cancel("description");

            ClearMembers();
            ShowEmpty(string.Format(Localizer.Get(StringKeys.ObjectBrowserNoMatches), query));
            return;
        }

        HideEmpty();

        // The members list follows the search too: with a type already
        // selected, its members narrow down to the ones that match.
        if (_type is not null) ApplyMemberFilter();
    }

    private void OnSearchTextChanged(string? text)
    {
        if (_settingSearchText) return;

        // Cancelled before anything else: typing a letter and deleting it
        // returns the text to what is already applied, and the search for the
        // letter must not run later and answer a question nobody asks now.
        _searchDebounce?.Cancel();

        var query = (text ?? "").Trim();
        if (query == _query) return;

        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;

        // The delay keeps a burst of keystrokes down to one search; each
        // search over System.* costs enough to be worth not repeating.
        Fire(async () =>
        {
            try
            {
                await Task.Delay(SearchDelay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await ApplyQueryAsync(text ?? "");
        });
    }

    private async Task OnTypeSelectedAsync(object? row)
    {
        if (row is BrowserTypeRow type) await SelectTypeAsync(type.Symbol);
    }

    /// <summary>Selects a type: its members are listed and it is described.</summary>
    public async Task SelectTypeAsync(BrowserSymbol type)
    {
        _type = type;

        await DescribeAsync(type);
        await ReloadMembersAsync();
    }

    internal async Task ReloadMembersAsync()
    {
        if (_type is not { } type) return;

        var inherited = _inherited.IsChecked == true;

        var members = await _loader.LatestAsync(
            "members", (service, ct) => service.GetMembersAsync(type, inherited, ct));

        if (members is null || type != _type) return;

        _allMembers = members;
        ApplyMemberFilter();
    }

    private void ApplyMemberFilter()
    {
        if (_type is not { } type) return;

        _memberRows.Clear();

        foreach (var member in ObjectBrowserService.FilterMembers(type, _allMembers, _query))
            _memberRows.Add(new BrowserMemberRow(member));
    }

    private void ClearMembers()
    {
        _type = null;
        _allMembers = [];
        _memberRows.Clear();
        ShowDescriptionHint();
    }

    private async Task OnMemberSelectedAsync(object? row)
    {
        if (row is BrowserMemberRow member) await DescribeAsync(member.Symbol);
    }

    /// <summary>Shows the declaration, its container and its summary.</summary>
    public async Task DescribeAsync(BrowserSymbol symbol)
    {
        var description = await _loader.LatestAsync(
            "description", (service, ct) => service.DescribeAsync(symbol, ct));

        if (description is null) return;

        _signature.Text = description.Signature;

        var lines = new List<string>();
        if (description.DeclaringType is { Length: > 0 } container)
            lines.Add(string.Format(Localizer.Get(StringKeys.ObjectBrowserMemberOf), container));
        if (description.Namespace.Length > 0)
            lines.Add(string.Format(Localizer.Get(StringKeys.ObjectBrowserNamespace), description.Namespace));
        lines.Add(string.Format(Localizer.Get(StringKeys.ObjectBrowserAssembly), description.AssemblyName));
        if (description.Source is null)
            lines.Add(Localizer.Get(StringKeys.ObjectBrowserFromMetadata));

        _details.Text = string.Join(Environment.NewLine, lines);
        _summary.Text = description.Summary ?? "";
    }

    private void ShowDescriptionHint()
    {
        _signature.Text = "";
        _details.Text = Localizer.Get(StringKeys.ObjectBrowserSelectType);
        _summary.Text = "";
    }

    private void ShowEmpty(string message)
    {
        _empty.Message = message;
        _empty.IsVisible = true;
    }

    private void HideEmpty() => _empty.IsVisible = false;

    // ---- Navigation and commands -----------------------------------------

    private async Task ActivateTypeRowAsync(object? row)
    {
        switch (row)
        {
            case BrowserNamespaceRow ns:
                await ToggleNamespaceAsync(ns);
                break;

            case BrowserTypeRow type:
                await ActivateAsync(type.Symbol);
                break;
        }
    }

    /// <summary>
    /// Opens a symbol at its declaration when it is written in the solution.
    /// The place is worked out again against the current compilation: what
    /// the browser listed may predate edits that moved the declaration.
    /// One from a referenced assembly has no file to open: the description
    /// already shows its declaration, which is all there is to show.
    /// </summary>
    public async Task ActivateAsync(BrowserSymbol? symbol)
    {
        if (symbol?.Source is null) return;

        var target = await _loader.LatestAsync(
            "navigate", (service, ct) => service.ResolveSourceAsync(symbol, ct));

        if (target is not null) SourceRequested?.Invoke(this, target);
    }

    private void OnTypeListKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Fire(() => ActivateTypeRowAsync(_typeList.SelectedItem));
                e.Handled = true;
                break;

            case Key.Right or Key.Left when _typeList.SelectedItem is BrowserNamespaceRow { } ns:
                if (ns.IsExpanded == (e.Key == Key.Left)) Fire(() => ToggleNamespaceAsync(ns));
                e.Handled = true;
                break;
        }
    }

    private void OnMemberListKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        Fire(() => ActivateAsync(SelectedSymbol(_memberList.SelectedItem)));
        e.Handled = true;
    }

    private async Task CopySignatureAsync(BrowserSymbol? symbol)
    {
        if (symbol is null) return;

        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(symbol.Signature));

        await clipboard.SetDataAsync(transfer);
    }

    /// <summary>The text "Copy signature" puts on the clipboard, for the tests.</summary>
    internal static string SignatureToCopy(BrowserSymbol symbol) => symbol.Signature;

    /// <summary>
    /// Runs a question without awaiting it from an event handler. A failure is
    /// reported rather than lost: an exception in a fire-and-forget task would
    /// otherwise leave the panel looking as if nothing had been asked.
    /// </summary>
    private void Fire(Func<Task> work) => _ = RunAsync(work);

    private async Task RunAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex.Message);
        }
    }

    private sealed class TextObserver(Action<string?> onNext) : IObserver<string?>
    {
        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(string? value) => onNext(value);
    }
}
