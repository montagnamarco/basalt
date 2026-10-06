using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Workspace.ObjectBrowser;
using Microsoft.CodeAnalysis;

namespace Basalt.Tests;

/// <summary>
/// The Object Browser panel: what it lists, what it describes, and what
/// opening a symbol asks of the shell.
/// </summary>
public sealed class ObjectBrowserPanelTests
{
    private static ObjectBrowserPanel PanelFor(Compilation compilation, Action<int>? onWorkThread = null)
    {
        var service = new ObjectBrowserService(_ =>
        {
            onWorkThread?.Invoke(Environment.CurrentManagedThreadId);
            return Task.FromResult<IReadOnlyList<BrowserProject>>([new BrowserProject("ShopLib", compilation)]);
        }, pathExists: _ => true);

        return new ObjectBrowserPanel(new ObjectBrowserLoader(service)) { PathExists = _ => true };
    }

    private static async Task<ObjectBrowserPanel> LoadedAsync(Compilation? compilation = null)
    {
        var panel = PanelFor(compilation ?? ObjectBrowserTests.Compile());
        await panel.RefreshAsync();

        // The project is the second entry; "All" is first.
        await panel.SelectLibraryAsync(panel.Libraries.Single(l => l.Kind == BrowserLibraryKind.Project));
        return panel;
    }

    [AvaloniaFact]
    public async Task OffersAllTheProjectAndItsAssembliesInTheSelector()
    {
        var panel = PanelFor(ObjectBrowserTests.Compile());

        await panel.RefreshAsync();

        var labels = panel.LibraryBox.Items.Cast<string>().ToList();

        Assert.Equal("<All Libraries>", labels[0]);
        Assert.Equal("ShopLib", labels[1]);
        Assert.Contains("System.Private.CoreLib", labels);
        Assert.Equal(0, panel.LibraryBox.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task NamespacesFoldAndOnlyReadTheirTypesWhenOpened()
    {
        var panel = await LoadedAsync();

        var ns = Assert.IsType<BrowserNamespaceRow>(Assert.Single(panel.TypeRows));
        Assert.Equal("Shop", ns.Name);
        Assert.False(ns.IsExpanded);

        await panel.ToggleNamespaceAsync(ns);

        var rows = panel.TypeRows;
        Assert.True(Assert.IsType<BrowserNamespaceRow>(rows[0]).IsExpanded);
        var names = rows.OfType<BrowserTypeRow>().Select(row => row.Symbol.Name).ToList();
        Assert.Contains("Customer", names);
        Assert.Contains("Helpers", names);
        Assert.All(rows.OfType<BrowserTypeRow>(), row => Assert.True(row.UnderNamespace));

        await panel.ToggleNamespaceAsync((BrowserNamespaceRow)rows[0]);

        Assert.Single(panel.TypeRows);
        Assert.False(Assert.IsType<BrowserNamespaceRow>(panel.TypeRows[0]).IsExpanded);
    }

    [AvaloniaFact]
    public async Task SelectingATypeListsItsMembersAndDescribesIt()
    {
        var panel = await LoadedAsync();
        await panel.ToggleNamespaceAsync((BrowserNamespaceRow)panel.TypeRows[0]);
        var customer = panel.TypeRows.OfType<BrowserTypeRow>().Single(row => row.Symbol.Name == "Customer").Symbol;

        await panel.SelectTypeAsync(customer);

        Assert.Equal("Public Class Customer", panel.SignatureText);
        Assert.Contains("Namespace Shop", panel.DetailsText);
        Assert.Contains("Assembly ShopLib", panel.DetailsText);
        Assert.Equal("A person who buys things.", panel.SummaryText);
        Assert.Contains(panel.MemberRows, row => row.Symbol.Signature == "Public Sub Show(text As String)");

        await panel.DescribeAsync(panel.MemberRows.Single(row => row.Symbol.Name == "Show").Symbol);

        Assert.Equal("Public Sub Show(text As String)", panel.SignatureText);
        Assert.Contains("Member of Customer", panel.DetailsText);
        Assert.Equal("Shows the text in a box, see Greet.", panel.SummaryText);
    }

    [AvaloniaFact]
    public async Task TheInheritedToggleAddsTheMembersOfTheBaseTypes()
    {
        var panel = await LoadedAsync();
        await panel.ToggleNamespaceAsync((BrowserNamespaceRow)panel.TypeRows[0]);
        var customer = panel.TypeRows.OfType<BrowserTypeRow>().Single(row => row.Symbol.Name == "Customer").Symbol;
        await panel.SelectTypeAsync(customer);

        Assert.DoesNotContain(panel.MemberRows, row => row.Symbol.Name == "Greet");

        panel.InheritedToggle.IsChecked = true;
        await panel.ReloadMembersAsync();

        Assert.Contains(panel.MemberRows, row => row.Symbol is { Name: "Greet", IsInherited: true });

        panel.InheritedToggle.IsChecked = false;
        await panel.ReloadMembersAsync();

        Assert.DoesNotContain(panel.MemberRows, row => row.Symbol.Name == "Greet");
    }

    [AvaloniaFact]
    public async Task SearchReplacesTheTreeWithMatchingTypesAndNarrowsTheMembers()
    {
        var panel = await LoadedAsync();
        await panel.ToggleNamespaceAsync((BrowserNamespaceRow)panel.TypeRows[0]);
        var customer = panel.TypeRows.OfType<BrowserTypeRow>().Single(row => row.Symbol.Name == "Customer").Symbol;
        await panel.SelectTypeAsync(customer);

        await panel.SetQueryAsync("show");

        var type = Assert.IsType<BrowserTypeRow>(Assert.Single(panel.TypeRows));
        Assert.Equal("Customer", type.Symbol.Name);
        Assert.False(type.UnderNamespace);
        Assert.Equal(["Show"], panel.MemberRows.Select(row => row.Symbol.Name));

        await panel.SetQueryAsync("zzz-nothing");

        Assert.Empty(panel.TypeRows);
        Assert.True(panel.ShowsEmptyState);

        await panel.SetQueryAsync("");

        Assert.IsType<BrowserNamespaceRow>(Assert.Single(panel.TypeRows));
        Assert.False(panel.ShowsEmptyState);
    }

    [AvaloniaFact]
    public async Task OpeningASourceSymbolAsksTheShellForItsFileAndLine()
    {
        var panel = await LoadedAsync();
        await panel.ToggleNamespaceAsync((BrowserNamespaceRow)panel.TypeRows[0]);
        var customer = panel.TypeRows.OfType<BrowserTypeRow>().Single(row => row.Symbol.Name == "Customer").Symbol;
        await panel.SelectTypeAsync(customer);

        var requests = new List<BrowserSourceTarget>();
        panel.SourceRequested += (_, target) => requests.Add(target);

        await panel.ActivateAsync(panel.MemberRows.Single(row => row.Symbol.Name == "Show").Symbol);

        var request = Assert.Single(requests);
        Assert.Equal(ObjectBrowserTests.SourcePath, request.FilePath);
        Assert.Equal(ObjectBrowserTests.ShowLine, request.Line);
        Assert.Equal(
            ObjectBrowserTests.Source.Split('\n')[ObjectBrowserTests.ShowLine - 1].IndexOf("Show", StringComparison.Ordinal) + 1,
            request.Column);
    }

    [AvaloniaFact]
    public async Task OpeningAMetadataSymbolOnlyShowsItsSignature()
    {
        var panel = PanelFor(ObjectBrowserTests.Compile());
        await panel.RefreshAsync();
        var core = panel.Libraries.Single(l => l.Name == "System.Private.CoreLib");
        await panel.SelectLibraryAsync(core);
        await panel.SetQueryAsync("StringBuilder");
        var builder = panel.TypeRows.OfType<BrowserTypeRow>().First(row => row.Symbol.Name == "StringBuilder").Symbol;

        var requests = new List<BrowserSourceTarget>();
        panel.SourceRequested += (_, target) => requests.Add(target);

        await panel.SelectTypeAsync(builder);
        await panel.ActivateAsync(builder);

        Assert.Empty(requests);
        Assert.Contains("Public NotInheritable Class StringBuilder", panel.SignatureText);
        Assert.Contains("no source", panel.DetailsText);
    }

    [AvaloniaFact]
    public async Task TypingAndDeletingALetterWithinTheDelayLeavesTheBoxAndTheListAlone()
    {
        var panel = await LoadedAsync();

        panel.SearchBox.Text = "c";
        panel.SearchBox.Text = "";

        // Longer than the typing delay: a search for "c" that was not
        // cancelled would have run and written its text back by now.
        await Task.Delay(600);

        Assert.Equal("", panel.SearchBox.Text ?? "");
        Assert.IsType<BrowserNamespaceRow>(Assert.Single(panel.TypeRows));
        Assert.False(panel.ShowsEmptyState);
    }

    [AvaloniaFact]
    public async Task AFoldedOrOpenedNamespaceKeepsTheKeyboardPosition()
    {
        var panel = await LoadedAsync();

        panel.TypeList.SelectedIndex = 0;
        await panel.ToggleNamespaceAsync((BrowserNamespaceRow)panel.TypeRows[0]);

        var opened = Assert.IsType<BrowserNamespaceRow>(panel.TypeList.SelectedItem);
        Assert.True(opened.IsExpanded);

        await panel.ToggleNamespaceAsync(opened);

        Assert.False(Assert.IsType<BrowserNamespaceRow>(panel.TypeList.SelectedItem).IsExpanded);
    }

    [AvaloniaFact]
    public async Task ASearchCutShortSaysSoInTheHeading()
    {
        var panel = PanelFor(ObjectBrowserTests.Compile());
        await panel.RefreshAsync();
        await panel.SelectLibraryAsync(panel.Libraries.Single(l => l.Name == "System.Private.CoreLib"));

        await panel.SetQueryAsync("e");

        Assert.Equal(BrowserSearchResult.Cap, panel.TypeRows.Count);
        Assert.StartsWith("Types - showing the first 1000 of ", panel.TypesHeaderText, StringComparison.Ordinal);

        await panel.SetQueryAsync("");

        Assert.Equal("Types", panel.TypesHeaderText);
    }

    [AvaloniaFact]
    public async Task AHiddenBrowserReadsAgainWhenItIsShownNotWhileItIsOutOfSight()
    {
        var panel = await LoadedAsync();

        // Never attached to a window, so effectively not visible.
        panel.ScheduleRefresh();

        Assert.True(panel.IsStale);

        await panel.RefreshIfStaleAsync();

        Assert.False(panel.IsStale);
    }

    [AvaloniaFact]
    public async Task TheContextMenuCopiesTheSignatureAndOnlyOffersSourceWhereThereIsSome()
    {
        var panel = await LoadedAsync();
        await panel.ToggleNamespaceAsync((BrowserNamespaceRow)panel.TypeRows[0]);

        var menu = panel.MemberList.ContextMenu!;
        var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToList();

        Assert.Equal(["Copy Signature", "Go to Source"], headers);
    }

    [AvaloniaFact]
    public async Task TheCompilerIsAskedOffTheInterfaceThreadAndTheAnswerIsAppliedOnIt()
    {
        var interfaceThread = Environment.CurrentManagedThreadId;
        var asked = new List<int>();

        var panel = PanelFor(ObjectBrowserTests.Compile(), asked.Add);

        await panel.RefreshAsync();

        Assert.Equal("ShopLib", panel.LibraryBox.Items.Cast<string>().ElementAt(1));

        var thread = Assert.Single(asked);
        Assert.NotEqual(interfaceThread, thread);
        Assert.True(Dispatcher.UIThread.CheckAccess());
    }

    [AvaloniaFact]
    public async Task ALargeReferencedNamespaceStaysVirtualised()
    {
        var panel = PanelFor(ObjectBrowserTests.Compile());
        await panel.RefreshAsync();
        await panel.SelectLibraryAsync(panel.Libraries.Single(l => l.Name == "System.Private.CoreLib"));

        var window = new Window { Content = panel, Width = 700, Height = 500 };
        window.Show();

        var system = panel.TypeRows.OfType<BrowserNamespaceRow>().Single(row => row.Name == "System");
        await panel.ToggleNamespaceAsync(system);
        window.UpdateLayout();

        var rows = panel.TypeRows.Count;
        var realised = panel.TypeList.GetVisualDescendants().OfType<ListBoxItem>().Count();

        Assert.True(rows > 300, $"System should hold hundreds of types, found {rows} rows.");
        Assert.InRange(realised, 1, 80);

        window.Close();
    }

    [AvaloniaFact]
    public async Task ARefreshKeepsTheSelectedTypeMemberAndScrollPosition()
    {
        var panel = PanelFor(ObjectBrowserTests.Compile());
        await panel.RefreshAsync();
        await panel.SelectLibraryAsync(panel.Libraries.Single(l => l.Name == "System.Private.CoreLib"));

        var window = new Window { Content = panel, Width = 700, Height = 500 };
        window.Show();

        await panel.ToggleNamespaceAsync(panel.TypeRows.OfType<BrowserNamespaceRow>().Single(r => r.Name == "System"));
        window.UpdateLayout();

        var math = panel.TypeRows.OfType<BrowserTypeRow>().Single(r => r.Symbol.Name == "Math");
        panel.TypeList.SelectedItem = math;
        panel.TypeList.ScrollIntoView(math);
        await panel.SelectTypeAsync(math.Symbol);
        window.UpdateLayout();

        var abs = panel.MemberRows.First(r => r.Symbol.Name == "Max");
        panel.MemberList.SelectedItem = abs;
        await panel.DescribeAsync(abs.Symbol);

        var scroller = panel.TypeList.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Offset = new Avalonia.Vector(0, 300);
        window.UpdateLayout();
        var before = scroller.Offset.Y;
        Assert.True(before > 0);

        await panel.RefreshAsync();
        await Task.Delay(100);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.Equal("Math", ((BrowserTypeRow)panel.TypeList.SelectedItem!).Symbol.Name);
        Assert.Equal(abs.Symbol.Signature, ((BrowserMemberRow)panel.MemberList.SelectedItem!).Symbol.Signature);
        Assert.Equal(before, panel.TypeList.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.Y, 1);

        window.Close();
    }

    [AvaloniaFact]
    public async Task ARefreshUnderASearchKeepsTheSearchAndTheSelection()
    {
        var panel = PanelFor(ObjectBrowserTests.Compile());
        await panel.RefreshAsync();
        await panel.SelectLibraryAsync(panel.Libraries.Single(l => l.Name == "System.Private.CoreLib"));
        await panel.SetQueryAsync("StringBuilder");

        var builder = panel.TypeRows.OfType<BrowserTypeRow>().First(r => r.Symbol.Name == "StringBuilder");
        panel.TypeList.SelectedItem = builder;
        await panel.SelectTypeAsync(builder.Symbol);

        await panel.RefreshAsync();

        Assert.Equal("StringBuilder", panel.SearchBox.Text);
        Assert.Equal("StringBuilder", ((BrowserTypeRow)panel.TypeList.SelectedItem!).Symbol.Name);
        Assert.NotEmpty(panel.MemberRows);
    }

    [AvaloniaFact]
    public async Task ABrowserThatWasStaleReadsAgainAsItsTabIsShown()
    {
        var calls = 0;
        var panel = PanelFor(ObjectBrowserTests.Compile(), _ => calls++);
        await panel.RefreshAsync();

        panel.ScheduleRefresh();
        Assert.True(panel.IsStale);
        var before = calls;

        var window = new Window { Content = panel, Width = 600, Height = 400 };
        window.Show();
        await Task.Delay(300);

        Assert.False(panel.IsStale);
        Assert.True(calls > before, "Showing the stale browser should have read the libraries again.");

        window.Close();
    }

    [AvaloniaFact]
    public async Task ASearchTypedDuringARefreshIsNotOverwrittenByTheOldRows()
    {
        var panel = await LoadedAsync();
        await panel.ToggleNamespaceAsync((BrowserNamespaceRow)panel.TypeRows[0]);

        panel.AfterFetchForTests = async () =>
        {
            panel.AfterFetchForTests = null;
            await panel.SetQueryAsync("Customer");
        };

        await panel.RefreshAsync();

        var row = Assert.IsType<BrowserTypeRow>(Assert.Single(panel.TypeRows));
        Assert.Equal("Customer", row.Symbol.Name);
        Assert.True(panel.IsStale, "The abandoned refresh should have been put off, not lost.");
    }

    [AvaloniaFact]
    public async Task ATypeChosenDuringARefreshStaysChosen()
    {
        var panel = await LoadedAsync();
        await panel.ToggleNamespaceAsync((BrowserNamespaceRow)panel.TypeRows[0]);
        var types = panel.TypeRows.OfType<BrowserTypeRow>().ToList();
        var customer = types.Single(r => r.Symbol.Name == "Customer").Symbol;
        var person = types.Single(r => r.Symbol.Name == "Person").Symbol;
        await panel.SelectTypeAsync(customer);

        panel.AfterFetchForTests = async () =>
        {
            panel.AfterFetchForTests = null;
            await panel.SelectTypeAsync(person);
        };

        await panel.RefreshAsync();

        Assert.Equal("Public Class Person", panel.SignatureText);
        Assert.Contains(panel.MemberRows, r => r.Symbol.Name == "Greet");
        Assert.True(panel.IsStale);
    }
}
