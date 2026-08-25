using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// What the completion list holds while a word is being typed.
///
/// Typing "Console." fired five requests at the same caret. One answered with
/// Console's members; the others answered with everything in scope, and
/// whichever finished last won — so the member list disappeared under a list
/// of 3854 entries.
/// </summary>
public sealed class CompletionRaceTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-race-").FullName;

    private MainWindowViewModel _vm = null!;

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    private const string Code =
        "Module Program\n    Sub Main()\n        \n    End Sub\nEnd Module\n";

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(ProgramPath, Code);

        _vm = new MainWindowViewModel();

        await _vm.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }

    private CodeEditor Open(out Window window)
    {
        var document = new EditorDocumentViewModel(ProgramPath, Code);

        var editor = new CodeEditor(document, _vm);

        window = new Window { Content = editor, Width = 700, Height = 500 };

        window.Show();
        window.UpdateLayout();

        // On the blank line inside Main.
        editor.CaretOffsetForTests = Code.IndexOf("        \n", StringComparison.Ordinal) + 8;

        return editor;
    }

    [AvaloniaFact]
    public async Task TypingAWordAsksOnceRatherThanOncePerLetter()
    {
        var editor = Open(out var window);

        editor.TypeForTests("Console");

        await Task.Delay(900);

        // Seven letters used to mean seven round trips through Roslyn.
        Assert.True(editor.CompletionRequestsForTests <= 2,
            $"typing seven letters asked {editor.CompletionRequestsForTests} times");

        window.Close();
    }

    [AvaloniaFact]
    public async Task AfterADotTheListHoldsTheMembersOfWhatCameBefore()
    {
        var editor = Open(out var window);

        editor.TypeForTests("Console.");

        await Task.Delay(1200);

        var offered = editor.LastCompletionCountForTests;

        // Console has some fifty members. Everything in scope is thousands:
        // that is the wrong answer winning the race.
        Assert.True(offered is > 0 and < 500,
            $"the list holds {offered} entries, which is not Console's members");

        window.Close();
    }

    [AvaloniaFact]
    public async Task NoAnswerIsShownForACaretThatHasMovedOn()
    {
        var editor = Open(out var window);

        var wasAt = editor.CaretOffsetForTests;

        editor.TypeForTests("Console.");

        // Away before the answer can arrive.
        editor.CaretOffsetForTests = 0;

        await Task.Delay(1200);

        // Whatever is shown belongs to where the caret is now, not to the
        // place it was asked from: 3854 names in scope would mean the stale
        // answer won.
        var offered = editor.LastCompletionCountForTests;

        Assert.True(offered < 500,
            $"a list of {offered} entries was shown, which is the stale answer");

        window.Close();
    }
}
