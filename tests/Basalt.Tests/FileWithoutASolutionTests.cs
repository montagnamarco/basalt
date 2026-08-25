using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// A file opened on its own, with no solution behind it.
///
/// The commonest way to try the IDE out, and the one case every test missed:
/// they all opened a solution first. Without one the file goes into a project
/// built by hand, and that project had no global imports — so "Console" was
/// an undeclared name, typing "Console." offered nothing, and hovering it
/// said nothing. Both symptoms, one cause.
/// </summary>
public sealed class FileWithoutASolutionTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-nosln-").FullName;

    private const string Code =
        "Module Program\n    Sub Main()\n        Console\n    End Sub\nEnd Module\n";

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    public FileWithoutASolutionTests() => File.WriteAllText(ProgramPath, Code);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static int AfterConsole =>
        Code.IndexOf("Console", StringComparison.Ordinal) + "Console".Length;

    [AvaloniaFact]
    public async Task ADotOffersTheMembersOfWhatCameBefore()
    {
        var vm = new MainWindowViewModel();

        await vm.OpenFileAsync(ProgramPath);

        var withDot = Code.Insert(AfterConsole, ".");

        var items = await vm.GetCompletionsAsync(ProgramPath, AfterConsole + 1, withDot);

        // Console has some fifty members. Nothing at all was the bug.
        Assert.InRange(items.Count, 1, 500);
        Assert.Contains(items, i => i.DisplayText == "WriteLine");
    }

    [AvaloniaFact]
    public async Task ASymbolCanStillBeDescribed()
    {
        var vm = new MainWindowViewModel();

        await vm.OpenFileAsync(ProgramPath);

        var description = await vm.DescribeSymbolAsync(
            ProgramPath, AfterConsole - 2, Code);

        Assert.NotNull(description);
        Assert.Contains("Console", description.PlainSignature, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TypingADotInTheEditorOpensTheList()
    {
        using var host = new TestWindow();

        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync();

        var editor = host.Window.GetVisualDescendants().OfType<CodeEditor>().First();

        editor.CaretOffsetForTests = AfterConsole;

        host.Window.KeyTextInput(".");

        await Task.Delay(1200);

        Assert.True(editor.CompletionRequestsForTests > 0, "nothing was asked");
        Assert.InRange(editor.LastCompletionOffered, 1, 500);
    }

    [AvaloniaFact]
    public async Task TheFrameworkIsThereNotJustTheCoreLibrary()
    {
        // Referencing only the assembly holding Object answered for String
        // and Integer and for nothing else.
        var vm = new MainWindowViewModel();

        await vm.OpenFileAsync(ProgramPath);

        var withDot = Code.Replace("Console", "System.Text.StringBuilder.");

        var at = withDot.IndexOf("StringBuilder.", StringComparison.Ordinal)
               + "StringBuilder.".Length;

        var items = await vm.GetCompletionsAsync(ProgramPath, at, withDot);

        Assert.NotEmpty(items);
    }
}
