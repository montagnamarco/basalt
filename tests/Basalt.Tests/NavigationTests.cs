using Avalonia.Headless.XUnit;
using Basalt.Extensibility;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>Where the user has been, and stepping through it.</summary>
public class NavigationHistoryTests
{
    private static NavigationPoint At(string file, int line) => new(file, line * 10, line);

    [Fact]
    public void StartsWithNowhereToGo()
    {
        var history = new NavigationHistory();

        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
        Assert.Null(history.Current);
    }

    [Fact]
    public void StepsBackThroughRecordedPlaces()
    {
        var history = new NavigationHistory();
        history.Record(At("A.vb", 1));
        history.Record(At("B.vb", 2));

        Assert.True(history.CanGoBack);
        Assert.Equal("A.vb", history.GoBack()!.Value.FilePath);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void StepsForwardAfterSteppingBack()
    {
        var history = new NavigationHistory();
        history.Record(At("A.vb", 1));
        history.Record(At("B.vb", 2));
        history.GoBack();

        Assert.True(history.CanGoForward);
        Assert.Equal("B.vb", history.GoForward()!.Value.FilePath);
    }

    [Fact]
    public void DiscardsTheForwardPathWhenANewJumpIsMade()
    {
        // Like a browser: jumping somewhere new leaves the old forward path
        // behind rather than keeping a branch the user cannot see.
        var history = new NavigationHistory();
        history.Record(At("A.vb", 1));
        history.Record(At("B.vb", 2));
        history.GoBack();

        history.Record(At("C.vb", 3));

        Assert.False(history.CanGoForward);
        Assert.Equal("C.vb", history.Current!.Value.FilePath);
    }

    [Fact]
    public void IgnoresMovementWithinTheSameLine()
    {
        // Typing would otherwise fill the history with keystrokes.
        var history = new NavigationHistory();
        history.Record(At("A.vb", 5));
        history.Record(new NavigationPoint("A.vb", 57, 5));

        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void DropsTheOldestPlaceOnceFull()
    {
        var history = new NavigationHistory { Capacity = 3 };

        for (var line = 1; line <= 5; line++) history.Record(At("A.vb", line));

        // Only the last three survive, so stepping back twice reaches the third.
        history.GoBack();
        history.GoBack();

        Assert.Equal(3, history.Current!.Value.Line);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void SignalsWhenThePositionChanges()
    {
        var history = new NavigationHistory();
        var raised = 0;
        history.Changed += (_, _) => raised++;

        history.Record(At("A.vb", 1));
        history.Record(At("B.vb", 2));
        history.GoBack();

        Assert.Equal(3, raised);
    }
}

/// <summary>The outline of the open document.</summary>
public class OutlinePanelTests
{
    private static DocumentSymbol Symbol(string name, SymbolKind kind, int start, int end,
        params DocumentSymbol[] children) =>
        new(name, kind, new SourceRange(new SourcePosition(start, 1), new SourcePosition(end, 1)))
        {
            Children = children
        };

    [AvaloniaFact]
    public void ShowsNothingForADocumentWithNoDeclarations()
    {
        var panel = new OutlinePanel();
        panel.Show([]);

        Assert.Empty(panel.Symbols);
    }

    [AvaloniaFact]
    public void ListsSymbolsAsGiven()
    {
        var panel = new OutlinePanel();

        panel.Show([
            Symbol("Customer", SymbolKind.Class, 1, 20,
                Symbol("Name", SymbolKind.Property, 2, 2),
                Symbol("Save", SymbolKind.Method, 4, 8))
        ]);

        var customer = Assert.Single(panel.Symbols);
        Assert.Equal(2, customer.Children.Count);
    }

    [AvaloniaFact]
    public void FindsTheInnermostSymbolContainingALine()
    {
        // Following the caret means picking the method, not the class it is in.
        var panel = new OutlinePanel();

        panel.Show([
            Symbol("Customer", SymbolKind.Class, 1, 20,
                Symbol("Save", SymbolKind.Method, 4, 8))
        ]);

        Assert.Equal("Save", panel.SymbolAtLine(6)?.Name);
        Assert.Equal("Customer", panel.SymbolAtLine(15)?.Name);
        Assert.Null(panel.SymbolAtLine(50));
    }

    [AvaloniaFact]
    public void ClearsWhenAskedTo()
    {
        var panel = new OutlinePanel();
        panel.Show([Symbol("A", SymbolKind.Class, 1, 2)]);

        panel.Clear();

        Assert.Empty(panel.Symbols);
    }
}

/// <summary>Listing where a symbol is used.</summary>
public sealed class ReferencesPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-refs", Guid.NewGuid().ToString("N"));

    public ReferencesPanelTests() => Directory.CreateDirectory(_root);

    [AvaloniaFact]
    public async Task ShowsAPreviewLineForEachReference()
    {
        var file = Path.Combine(_root, "A.vb");
        await File.WriteAllTextAsync(file, "Public Class A\n    Dim target As Integer\nEnd Class");

        var panel = new ReferencesPanel();

        await panel.ShowAsync("target", [
            new SourceLocation(file, SourceRange.At(new SourcePosition(2, 9)))
        ]);

        var row = Assert.Single(panel.Rows);
        Assert.Equal("Dim target As Integer", row.Preview);
        Assert.Contains("A.vb(2)", row.Display);
    }

    [AvaloniaFact]
    public async Task SaysWhenThereAreNone()
    {
        var panel = new ReferencesPanel();

        await panel.ShowAsync("missing", []);

        Assert.Contains("No references to 'missing'", panel.Summary);
    }

    [AvaloniaFact]
    public async Task CountsWhatItFound()
    {
        var file = Path.Combine(_root, "B.vb");
        await File.WriteAllTextAsync(file, "one\ntwo\nthree");

        var panel = new ReferencesPanel();

        await panel.ShowAsync("x", [
            new SourceLocation(file, SourceRange.At(new SourcePosition(1, 1))),
            new SourceLocation(file, SourceRange.At(new SourcePosition(3, 1)))
        ]);

        Assert.Contains("2 references", panel.Summary);
    }

    [AvaloniaFact]
    public async Task SurvivesAReferenceToAFileThatIsGone()
    {
        var panel = new ReferencesPanel();

        await panel.ShowAsync("x", [
            new SourceLocation(
                Path.Combine(_root, "Deleted.vb"), SourceRange.At(new SourcePosition(1, 1)))
        ]);

        var row = Assert.Single(panel.Rows);
        Assert.Equal("", row.Preview);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}

/// <summary>Jumping to a symbol by name.</summary>
public class GoToSymbolDialogTests
{
    private static readonly SymbolChoice[] Symbols =
    [
        new("Customer", SymbolKind.Class, "/A.vb", 1),
        new("CustomerRepository", SymbolKind.Class, "/B.vb", 1),
        new("AccountCustomer", SymbolKind.Class, "/C.vb", 1),
        new("Save", SymbolKind.Method, "/A.vb", 10)
    ];

    [AvaloniaFact]
    public void OffersEverySymbolBeforeAnythingIsTyped()
    {
        var dialog = new GoToSymbolDialog(Symbols, "Go to Symbol");

        Assert.Equal(4, dialog.Choices.Count);
    }

    [AvaloniaFact]
    public void NarrowsToNamesContainingTheQuery()
    {
        var dialog = new GoToSymbolDialog(Symbols, "Go to Symbol");

        dialog.FilterForTests("Customer");

        Assert.Equal(3, dialog.Choices.Count);
        Assert.DoesNotContain(dialog.Choices, c => c.Name == "Save");
    }

    [AvaloniaFact]
    public void PutsNamesStartingWithTheQueryFirst()
    {
        // Typing "Cust" should offer "Customer" before "AccountCustomer".
        var dialog = new GoToSymbolDialog(Symbols, "Go to Symbol");

        dialog.FilterForTests("Cust");

        Assert.Equal("Customer", dialog.Choices[0].Name);
    }

    [AvaloniaFact]
    public void MatchesRegardlessOfCase()
    {
        var dialog = new GoToSymbolDialog(Symbols, "Go to Symbol");

        dialog.FilterForTests("customer");

        Assert.NotEmpty(dialog.Choices);
    }

    [AvaloniaFact]
    public void ShowsWhereEachSymbolIs()
    {
        var dialog = new GoToSymbolDialog(Symbols, "Go to Symbol");

        Assert.Equal("A.vb:10", dialog.Choices.Single(c => c.Name == "Save").Detail);
    }
}
