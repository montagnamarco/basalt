using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Typing into a file that has just been opened.
///
/// Every earlier test raised the input event itself, and so proved only that
/// the handler worked. Through a real keystroke nothing happened at all: an
/// opened file was never given the keyboard, so the dot went nowhere, the
/// completion list never opened and no tooltip ever appeared.
/// </summary>
public sealed class TypingInAFreshFileTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-fresh-").FullName;

    private const string Code =
        "Module Program\n    Sub Main()\n        Console\n    End Sub\nEnd Module\n";

    private string ProgramPath => Path.Combine(_root, "Program.vb");

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
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }

    private async Task<(TestWindow Host, CodeEditor Editor)> OpenAsync()
    {
        var host = new TestWindow();

        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync();

        var editor = host.Window.GetVisualDescendants().OfType<CodeEditor>().First();

        return (host, editor);
    }

    [AvaloniaFact]
    public async Task AKeystrokeReachesAFileJustOpened()
    {
        var (host, editor) = await OpenAsync();

        editor.CaretOffsetForTests =
            Code.IndexOf("Console", StringComparison.Ordinal) + "Console".Length;

        // Delivered by the platform, with no help from the test.
        host.Window.KeyTextInput(".");

        await Task.Delay(200);

        Assert.Contains("Console.", editor.TextForTests, StringComparison.Ordinal);

        host.Dispose();
    }

    [AvaloniaFact]
    public async Task TypingADotOpensTheMemberList()
    {
        var (host, editor) = await OpenAsync();

        editor.CaretOffsetForTests =
            Code.IndexOf("Console", StringComparison.Ordinal) + "Console".Length;

        host.Window.KeyTextInput(".");

        await Task.Delay(1000);

        Assert.True(editor.CompletionRequestsForTests > 0,
            "typing a dot asked for nothing");

        // Console's members, not the thousands of names in scope.
        Assert.InRange(editor.LastCompletionOffered, 1, 500);

        host.Dispose();
    }

    [AvaloniaFact]
    public async Task TheDescriptionOfASymbolCanBeAsked()
    {
        // The pointer is not the only way: a hover needs a hand on the mouse,
        // and the answer is wanted most while typing.
        var (host, editor) = await OpenAsync();

        editor.CaretOffsetForTests =
            Code.IndexOf("Console", StringComparison.Ordinal) + 2;

        Assert.True(await editor.ShowHoverAtCaretAsync());

        host.Dispose();
    }

    [AvaloniaFact]
    public async Task AskingAboutNothingSaysNothing()
    {
        var (host, editor) = await OpenAsync();

        // On the blank run before Console.
        editor.CaretOffsetForTests = Code.IndexOf("    Sub", StringComparison.Ordinal);

        Assert.False(await editor.ShowHoverAtCaretAsync());

        host.Dispose();
    }
}
