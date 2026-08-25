using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>The menu shown on a right click in the editor.</summary>
public sealed class EditorContextMenuTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-ctxmenu", Guid.NewGuid().ToString("N"));

    public EditorContextMenuTests() => Directory.CreateDirectory(_root);

    private async Task<(TestWindow Host, CodeEditor Code, TextEditor Editor)> OpenAsync()
    {
        var file = Path.Combine(_root, "Program.vb");

        await File.WriteAllTextAsync(file, "Module A\n    Sub M()\n        Dim x = 1\n    End Sub\nEnd Module");

        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var code = host.Window.GetVisualDescendants().OfType<CodeEditor>().Single();
        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();

        return (host, code, editor);
    }

    [AvaloniaFact]
    public async Task TheEditorHasAContextMenu()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        Assert.NotNull(code.EditorMenu);
        Assert.NotNull(editor.ContextMenu);
    }

    [AvaloniaFact]
    public async Task OffersTheCommandsAnEditorIsExpectedToHave()
    {
        var (host, code, _) = await OpenAsync();
        using var __ = host;

        var headers = code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Select(i => i.Header?.ToString())
            .ToList();

        Assert.Contains("Cut", headers);
        Assert.Contains("Go to Definition", headers);
        Assert.Contains("Find References", headers);
        Assert.Contains("Format Document", headers);
        Assert.Contains("Toggle Breakpoint", headers);
    }

    [AvaloniaFact]
    public async Task GreysOutCuttingWhenNothingIsSelected()
    {
        // Disabled rather than hidden: a menu whose entries move about has to
        // be read every time.
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        editor.SelectionLength = 0;
        code.EditorMenu.UpdateAvailabilityForTests();

        Assert.False(code.EditorMenu.IsEnabled(EditorCommand.Cut));
        Assert.False(code.EditorMenu.IsEnabled(EditorCommand.FormatSelection));
    }

    [AvaloniaFact]
    public async Task AllowsCuttingWhenSomethingIsSelected()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        editor.SelectionStart = 0;
        editor.SelectionLength = 6;

        code.EditorMenu.UpdateAvailabilityForTests();

        Assert.True(code.EditorMenu.IsEnabled(EditorCommand.Cut));
        Assert.True(code.EditorMenu.IsEnabled(EditorCommand.Copy));
    }

    [AvaloniaFact]
    public async Task LeavesNavigationAvailableWhereverTheCaretIs()
    {
        // What the caret is on is decided when the command runs, not when the
        // menu opens: deciding early would mean parsing on every right click.
        var (host, code, _) = await OpenAsync();
        using var __ = host;

        code.EditorMenu.UpdateAvailabilityForTests();

        Assert.True(code.EditorMenu.IsEnabled(EditorCommand.GoToDefinition));
        Assert.True(code.EditorMenu.IsEnabled(EditorCommand.FindReferences));
    }

    [AvaloniaFact]
    public async Task ReportsWhatWasChosen()
    {
        var (host, code, _) = await OpenAsync();
        using var __ = host;

        EditorCommand? chosen = null;
        code.EditorMenu.CommandChosen += (_, command) => chosen = command;

        code.EditorMenu.Items[EditorCommand.Copy]
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(EditorCommand.Copy, chosen);
    }

    [AvaloniaFact]
    public async Task ShowsTheShortcutBesideTheCommand()
    {
        // The menu is where a user learns the keys.
        var (host, code, _) = await OpenAsync();
        using var __ = host;

        Assert.NotNull(code.EditorMenu.Items[EditorCommand.GoToDefinition].InputGesture);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [AvaloniaFact]
    public async Task OffersExtractVariableOnlyWhenSomethingIsSelected()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        editor.SelectionLength = 0;
        code.EditorMenu.UpdateAvailabilityForTests();

        Assert.False(code.EditorMenu.IsEnabled(EditorCommand.ExtractVariable));

        editor.SelectionStart = 0;
        editor.SelectionLength = 4;
        code.EditorMenu.UpdateAvailabilityForTests();

        Assert.True(code.EditorMenu.IsEnabled(EditorCommand.ExtractVariable));
    }

    [AvaloniaFact]
    public async Task ClickingExtractVariableRaisesItsCommand()
    {
        // The menu item has to reach the handler: an entry that looks right
        // and does nothing is the failure this test exists to catch.
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        EditorCommand? raised = null;

        code.EditorMenu.CommandChosen += (_, command) => raised = command;

        var item = code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == "Extract Variable…");

        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(EditorCommand.ExtractVariable, raised);
    }

    [AvaloniaFact]
    public async Task ClickingExtractMethodRaisesItsCommand()
    {
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        EditorCommand? raised = null;

        code.EditorMenu.CommandChosen += (_, command) => raised = command;

        var item = code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == "Extract Method…");

        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(EditorCommand.ExtractMethod, raised);
    }

    [AvaloniaFact]
    public async Task ClickingSortImportsRaisesItsCommand()
    {
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        EditorCommand? raised = null;

        code.EditorMenu.CommandChosen += (_, command) => raised = command;

        var item = code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == "Sort and Remove Imports");

        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(EditorCommand.TidyImports, raised);
    }

    [AvaloniaFact]
    public async Task OffersSortImportsWithoutASelection()
    {
        // Unlike extracting, this one works on the whole file.
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        editor.SelectionLength = 0;
        code.EditorMenu.UpdateAvailabilityForTests();

        Assert.True(code.EditorMenu.IsEnabled(EditorCommand.TidyImports));
    }

    [AvaloniaFact]
    public async Task ClickingExtractConstantRaisesItsCommand()
    {
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        EditorCommand? raised = null;

        code.EditorMenu.CommandChosen += (_, command) => raised = command;

        code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == "Extract Constant…")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(EditorCommand.ExtractConstant, raised);
    }

    [AvaloniaFact]
    public async Task ClickingInlineVariableRaisesItsCommand()
    {
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        EditorCommand? raised = null;

        code.EditorMenu.CommandChosen += (_, command) => raised = command;

        code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == "Inline Variable")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(EditorCommand.InlineVariable, raised);
    }

    [AvaloniaFact]
    public async Task OffersInliningWithoutASelection()
    {
        // The variable is wherever the caret is, so nothing has to be
        // selected first.
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        editor.SelectionLength = 0;
        code.EditorMenu.UpdateAvailabilityForTests();

        Assert.True(code.EditorMenu.IsEnabled(EditorCommand.InlineVariable));
        Assert.False(code.EditorMenu.IsEnabled(EditorCommand.ExtractConstant));
    }

    [AvaloniaFact]
    public async Task ClickingConvertConditionalRaisesItsCommand()
    {
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        EditorCommand? raised = null;

        code.EditorMenu.CommandChosen += (_, command) => raised = command;

        code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == "Convert If / Select Case")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(EditorCommand.ConvertConditional, raised);
    }

    [AvaloniaTheory]
    [InlineData("Generate Constructor…", EditorCommand.GenerateConstructor)]
    [InlineData("Generate Property…", EditorCommand.GenerateProperty)]
    public async Task ClickingAGeneratorRaisesItsCommand(string header, EditorCommand expected)
    {
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        EditorCommand? raised = null;

        code.EditorMenu.CommandChosen += (_, command) => raised = command;

        code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == header)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(expected, raised);
    }

    [AvaloniaTheory]
    [InlineData("Move Type to File…", EditorCommand.MoveTypeToFile)]
    [InlineData("Change Signature…", EditorCommand.ChangeSignature)]
    public async Task ClickingTheOtherRefactoringsRaisesTheirCommands(
        string header, EditorCommand expected)
    {
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        EditorCommand? raised = null;

        code.EditorMenu.CommandChosen += (_, command) => raised = command;

        code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == header)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(expected, raised);
    }

    [AvaloniaFact]
    public async Task NoIconIsUsedForTwoUnrelatedCommands()
    {
        // The rule that matters, and the opposite of the one this replaces.
        // Requiring an icon everywhere produced ten refactorings behind the
        // same wrench: ten items the eye cannot tell apart, where the icon
        // has stopped being a way to find a command. An icon is worth having
        // when it is unmistakable, and worth leaving out otherwise.
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        // The two assistant entries are one feature asked two ways, so they
        // share an icon on purpose. Everything else must be distinguishable.
        var byIcon = code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Where(i => i.Icon is IconView)
            .GroupBy(i => ((IconView)i.Icon!).Kind)
            .Where(g => g.Key != IconKind.Assistant && g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(i => i.Header))}")
            .ToList();

        Assert.True(byIcon.Count == 0,
            "These icons are shared by unrelated commands:\n" + string.Join("\n", byIcon));
    }

    [AvaloniaFact]
    public async Task TheCommandsWorthAnIconHaveOne()
    {
        // Cut, copy, paste and the debugger marks: the ones a reader finds by
        // shape rather than by reading. If these lose their icons the menu
        // has gone the other way, into a wall of text.
        var (host, code, _) = await OpenAsync();
        using var _2 = host;

        var withIcons = code.EditorMenu.Menu.ItemsSource!
            .OfType<MenuItem>()
            .Where(i => i.Icon is not null)
            .Select(i => i.Header?.ToString() ?? "")
            .ToList();

        Assert.Contains(withIcons, h => h.Contains("Cut", StringComparison.Ordinal));
        Assert.Contains(withIcons, h => h.Contains("Copy", StringComparison.Ordinal));
        Assert.Contains(withIcons, h => h.Contains("Breakpoint", StringComparison.Ordinal));
    }
}
