using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Settings;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// What every window owes the person using it.
///
/// A dialog that cannot be dismissed with Escape, or that can be resized
/// until its buttons are off-screen, is a dialog that fights back. These are
/// checked for each window rather than trusted, because the gaps were real:
/// four dialogs had no Escape and only one had a minimum size.
/// </summary>
public sealed class WindowChromeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-chrome", Guid.NewGuid().ToString("N"));

    public WindowChromeTests() => Directory.CreateDirectory(_root);

    /// <summary>Every dialog the IDE opens, built the way the IDE builds it.</summary>
    public static TheoryData<string> DialogNames() =>
    [
        "NewSolutionDialog", "NewWindowDialog", "ProjectPropertiesDialog",
        "SettingsWindow", "CommandPalette", "GoToSymbolDialog",
        "BreakpointConditionDialog", "ChangeSignatureDialog",
        "RefactoringPreviewDialog"
    ];

    private Window Build(string name) => name switch
    {
        "NewSolutionDialog" => new NewSolutionDialog(),
        "NewWindowDialog" => new NewWindowDialog(),
        "ProjectPropertiesDialog" => new ProjectPropertiesDialog(),
        "SettingsWindow" => new SettingsWindow(new SettingsStore(
            Path.Combine(_root, "settings.json")), null),
        "CommandPalette" => new CommandPalette(
            Basalt.Core.Commands.IdeCommands.CreateRegistry(
                Basalt.Core.Commands.KeyboardScheme.Basalt)),
        "GoToSymbolDialog" => new GoToSymbolDialog([], "Go to Symbol"),
        "BreakpointConditionDialog" => new BreakpointConditionDialog(
            new Basalt.Core.Services.Breakpoint("/a/b.vb", 1)),
        "ChangeSignatureDialog" => new ChangeSignatureDialog([("a", "Integer")]),
        "RefactoringPreviewDialog" => new RefactoringPreviewDialog(
            new Basalt.Workspace.Refactoring.RefactoringPreview("Test",
            [
                new Basalt.Workspace.Refactoring.FileChangePreview("/a/b.vb", "old", "new")
            ])),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown dialog.")
    };

    [AvaloniaTheory]
    [MemberData(nameof(DialogNames))]
    public void EveryWindowHasAFloorItCannotBeResizedBelow(string name)
    {
        // Without one, dragging a corner can put the buttons off-screen.
        var window = Build(name);

        Assert.True(window.MinWidth > 0, $"{name} has no minimum width.");
        Assert.True(window.MinHeight > 0, $"{name} has no minimum height.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(DialogNames))]
    public void TheFloorIsSmallerThanTheOpeningSize(string name)
    {
        // A minimum larger than the opening size would make the window jump
        // the moment it appears.
        var window = Build(name);

        if (window.Width > 0) Assert.True(window.MinWidth <= window.Width, name);
        if (window.Height > 0) Assert.True(window.MinHeight <= window.Height, name);
    }

    [AvaloniaTheory]
    [MemberData(nameof(DialogNames))]
    public void EveryWindowCanBeDismissedWithEscape(string name)
    {
        // Either a button marked as the cancel button, or a key handler that
        // watches for Escape. Four dialogs had neither.
        var window = Build(name);

        window.Show();

        var hasCancelButton = window.GetVisualDescendants()
            .OfType<Button>()
            .Any(b => b.IsCancel);

        Assert.True(hasCancelButton || HandlesEscape(name),
            $"{name} cannot be dismissed with Escape.");
    }

    /// <summary>
    /// The windows that watch for Escape themselves rather than marking a
    /// button, because they have no buttons to mark.
    /// </summary>
    private static bool HandlesEscape(string name) =>
        name is "CommandPalette" or "GoToSymbolDialog" or "SettingsWindow";

    [AvaloniaTheory]
    [MemberData(nameof(DialogNames))]
    public void EveryWindowOpensWhereTheUserIsLooking(string name)
    {
        // Centred on the window that opened it, rather than wherever the
        // system decides.
        var window = Build(name);

        Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
    }

    /// <summary>Every control in a window that the Tab key can reach.</summary>
    private static List<Control> Focusable(Window window)
    {
        var found = new List<Control>();

        void Walk(ILogical node)
        {
            if (node is Control { Focusable: true, IsTabStop: true } control)
                found.Add(control);

            if (node is ContentControl { Content: ILogical content })
                Walk(content);

            foreach (var child in node.LogicalChildren)
                Walk(child);
        }

        Walk(window);

        return found;
    }

    [AvaloniaTheory]
    [MemberData(nameof(DialogNames))]
    public void EveryWindowCanBeUsedFromTheKeyboardAlone(string name)
    {
        // A dialog with nothing tabbable is a dialog a keyboard user cannot
        // fill in.
        var window = Build(name);

        Assert.NotEmpty(Focusable(window));
    }

    [AvaloniaTheory]
    [MemberData(nameof(DialogNames))]
    public void NoWindowSkipsAControlInTheTabOrder(string name)
    {
        // TabIndex is either left alone everywhere, which means document
        // order, or set everywhere. Setting it on some controls and not
        // others puts the untouched ones first, which is never what was
        // meant.
        var window = Build(name);

        var indexes = Focusable(window)
            .Select(c => c.TabIndex)
            .Distinct()
            .ToList();

        if (indexes.Count == 1) return;

        Assert.DoesNotContain(0, indexes);
    }

    [AvaloniaTheory]
    [MemberData(nameof(DialogNames))]
    public void EveryWindowFocusesSomethingWhenItOpens(string name)
    {
        // Without this the first keystroke goes nowhere and the user has to
        // click before typing. Six of the nine dialogs used to open with
        // nothing focused.
        //
        // Read from the source rather than by showing the window: focus
        // needs a real window, and a headless one never gets there.
        var source = SourceOf(name);

        Assert.Contains("Opened +=", source, StringComparison.Ordinal);
        Assert.Contains(".Focus()", source, StringComparison.Ordinal);
    }

    /// <summary>The source of a dialog, whichever file it lives in.</summary>
    private static string SourceOf(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var shell = Path.Combine(directory.FullName, "src", "Basalt.Shell");

            if (Directory.Exists(shell))
            {
                foreach (var candidate in new[] { $"{name}.cs", $"{name}.axaml.cs" })
                {
                    var path = Path.Combine(shell, candidate);

                    if (File.Exists(path)) return File.ReadAllText(path);
                }

                Assert.Fail($"No source file was found for {name}.");
            }

            directory = directory.Parent;
        }

        Assert.Fail("The repository root was not found.");
        return "";
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
