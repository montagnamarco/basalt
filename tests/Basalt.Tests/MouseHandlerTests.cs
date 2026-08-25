using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Basalt.Core.Commands;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// What the mouse does, checked by clicking.
///
/// Double-clicking a row is how most of this IDE is navigated — a file in the
/// tree, a problem in the list, a reference — and none of those handlers was
/// exercised by a test. The same rule as the keyboard: a handler nothing
/// raises is a handler nobody has checked.
/// </summary>
public sealed class MouseHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-mouse", Guid.NewGuid().ToString("N"));

    public MouseHandlerTests() => Directory.CreateDirectory(_root);

    /// <summary>Double-clicks a control, as the user would.</summary>
    private static void DoubleClick(Control target) =>
        target.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

    [AvaloniaFact]
    public void DoubleClickingAFileInTheTreeOpensIt()
    {
        using var vm = new MainWindowViewModel();
        var panel = new SolutionExplorerPanel(vm);

        SolutionTreeNode? opened = null;
        panel.FileOpened += (_, node) => opened = node;

        var file = new SolutionTreeNode("Program.vb", "/a/Program.vb", NodeKind.SourceFile);

        panel.TreeForTests.ItemsSource = new[] { file };
        panel.TreeForTests.SelectedItem = file;

        DoubleClick(panel.TreeForTests);

        Assert.Same(file, opened);
    }

    [AvaloniaFact]
    public void DoubleClickingAFolderOpensNothing()
    {
        // A folder is not openable, and treating it as one would put an empty
        // editor on screen.
        using var vm = new MainWindowViewModel();
        var panel = new SolutionExplorerPanel(vm);

        SolutionTreeNode? opened = null;
        panel.FileOpened += (_, node) => opened = node;

        var folder = new SolutionTreeNode("Views", "/a/Views", NodeKind.Folder);

        panel.TreeForTests.ItemsSource = new[] { folder };
        panel.TreeForTests.SelectedItem = folder;

        DoubleClick(panel.TreeForTests);

        Assert.Null(opened);
    }

    [AvaloniaFact]
    public void DoubleClickingWithNothingSelectedOpensNothing()
    {
        using var vm = new MainWindowViewModel();
        var panel = new SolutionExplorerPanel(vm);

        SolutionTreeNode? opened = null;
        panel.FileOpened += (_, node) => opened = node;

        DoubleClick(panel.TreeForTests);

        Assert.Null(opened);
    }

    [AvaloniaFact]
    public void DoubleClickingAReferenceGoesToIt()
    {
        var panel = new ReferencesPanel();

        Basalt.Extensibility.SourceLocation? chosen = null;
        panel.ReferenceActivated += (_, location) => chosen = location;

        var where = new Basalt.Extensibility.SourceLocation("/a/Program.vb",
            Basalt.Extensibility.SourceRange.At(new Basalt.Extensibility.SourcePosition(12, 1)));

        var row = new ReferenceRow(where, "Dim x = 1");

        panel.ListForTests.ItemsSource = new[] { row };
        panel.ListForTests.SelectedItem = row;

        DoubleClick(panel.ListForTests);

        Assert.Equal(where, chosen);
    }

    [AvaloniaFact]
    public void DoubleClickingInThePaletteChoosesTheCommand()
    {
        var palette = new CommandPalette(IdeCommands.CreateRegistry(KeyboardScheme.Basalt));

        palette.Show();

        palette.AcceptForTests();

        Assert.NotNull(palette.Chosen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
