using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.Syntax;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Grammar-based colouring for the languages Roslyn does not cover.
///
/// The installation tests open a real editor: whether a grammar loads is not
/// something a table of extensions can answer.
/// </summary>
public sealed class TextMateHighlightingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-textmate", Guid.NewGuid().ToString("N"));

    public TextMateHighlightingTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("page.html")]
    [InlineData("site.css")]
    [InlineData("app.js")]
    [InlineData("view.cshtml")]
    [InlineData("view.vbhtml")]
    public void TakesResponsibilityForTheWebLanguages(string fileName)
    {
        Assert.True(TextMateHighlighting.Handles("/a/" + fileName));
    }

    [Fact]
    public void LeavesVisualBasicToRoslyn()
    {
        // Roslyn colours it knowing what the code means, not only how it is
        // spelt; a grammar would be a step backwards.
        Assert.False(TextMateHighlighting.Handles("/a/Program.vb"));
    }

    [Fact]
    public void ColoursCSharpWithAGrammarNow()
    {
        // It used to be left to Roslyn, which no longer offers it: C# is not
        // a language Basalt supports. A .cs file still turns up in a solution,
        // and a grammar keeps it readable as code rather than a wall of grey.
        Assert.True(TextMateHighlighting.Handles("/a/Program.cs"));
    }

    [Fact]
    public void ColoursRazorForVisualBasicWithTheGrammarThisProjectShips()
    {
        // No .vbhtml grammar exists anywhere else: ASP.NET Core never
        // supported the format. The one written for the VS Code extension is
        // used here too, so the two cannot colour a view differently.
        Assert.Equal("text.html.vbhtml", TextMateHighlighting.ScopeForFile("/a/view.vbhtml"));
    }

    [Fact]
    public void FindsTheSharedGrammarFile()
    {
        // Beside the IDE, where the VS Code extension keeps it. Its absence
        // would mean the two editors had quietly diverged.
        Assert.NotNull(VbHtmlGrammar.Path);
    }

    [Fact]
    public void CarriesTheGrammarInsideTheAssembly()
    {
        // The name of an embedded resource is not checked by the compiler: a
        // typo leaves Read() returning null, and .vbhtml used to fall back to
        // the C# Razor grammar — Visual Basic coloured as C#. Reading it out
        // of the assembly is the only way to know the name is right.
        using var stream = typeof(VbHtmlGrammar).Assembly
            .GetManifestResourceStream("Basalt.Shell.Syntax.vbhtml.tmLanguage.json");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var grammar = reader.ReadToEnd();

        Assert.Contains("text.html.vbhtml", grammar, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheGrammarEvenWithNothingOnDisk()
    {
        // What a stripped installation gets. Not empty, and the right one.
        var grammar = VbHtmlGrammar.Read();

        Assert.NotNull(grammar);
        Assert.Contains("text.html.vbhtml", grammar, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverFallsBackToTheCSharpRazorGrammar()
    {
        // The bug this replaced: a .vbhtml coloured with "text.html.cshtml"
        // reads its markup correctly and every code block as C#.
        Assert.NotEqual("text.html.cshtml",
            TextMateHighlighting.ScopeForFile("/a/view.vbhtml"));
    }

    [Fact]
    public void ReadsTheSharedGrammarAsAValidGrammar()
    {
        // Finding the file is not enough: a grammar that will not parse is
        // silently ignored, and the colouring falls back without saying so.
        var registry = new VbHtmlRegistryOptions(
            new TextMateSharp.Grammars.RegistryOptions(TextMateSharp.Grammars.ThemeName.LightPlus));

        Assert.NotNull(registry.GetGrammar("text.html.vbhtml"));
    }

    [Fact]
    public void StillServesTheOtherGrammars()
    {
        var registry = new VbHtmlRegistryOptions(
            new TextMateSharp.Grammars.RegistryOptions(TextMateSharp.Grammars.ThemeName.LightPlus));

        Assert.NotNull(registry.GetGrammar("source.css"));
        Assert.NotNull(registry.GetGrammar("text.html.derivative"));

        // Themes are looked up by theme name, not by grammar scope.
        Assert.NotNull(registry.GetDefaultTheme());
    }

    [Fact]
    public void FindsAGrammarForEachWebLanguage()
    {
        Assert.Equal("text.html.derivative", TextMateHighlighting.ScopeForFile("/a/page.html"));
        Assert.Equal("source.css", TextMateHighlighting.ScopeForFile("/a/site.css"));
        Assert.Equal("source.js", TextMateHighlighting.ScopeForFile("/a/app.js"));
    }

    [Fact]
    public void HasNothingToSayAboutAnUnknownFile()
    {
        Assert.Null(TextMateHighlighting.ScopeForFile("/a/notes.txt"));
    }

    [AvaloniaFact]
    public async Task InstallsOnARealEditor()
    {
        // Whether the grammar loads at all is the question; a table of
        // extensions cannot answer it.
        var file = Path.Combine(_root, "page.html");
        await File.WriteAllTextAsync(file, "<div class=\"a\">hello</div>");

        using var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var editor = host.Window.GetVisualDescendants()
            .OfType<CodeEditor>().Single()
            .GetVisualDescendants().OfType<TextEditor>().Single();

        // TextMate colours through a transformer rather than by setting
        // SyntaxHighlighting, so that property staying null is the sign it
        // took over.
        Assert.Null(editor.SyntaxHighlighting);
        Assert.NotEmpty(editor.TextArea.TextView.LineTransformers);
    }

    [AvaloniaFact]
    public async Task InstallsOnARazorViewForVisualBasic()
    {
        var file = Path.Combine(_root, "view.vbhtml");
        await File.WriteAllTextAsync(file, "@Code\n    Dim x = 1\nEnd Code\n<p>@x</p>");

        using var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var editor = host.Window.GetVisualDescendants()
            .OfType<CodeEditor>().Single()
            .GetVisualDescendants().OfType<TextEditor>().Single();

        Assert.Null(editor.SyntaxHighlighting);
        Assert.NotEmpty(editor.TextArea.TextView.LineTransformers);
    }

    [AvaloniaFact]
    public async Task LeavesVisualBasicToItsOwnHighlighting()
    {
        var file = Path.Combine(_root, "Program.vb");
        await File.WriteAllTextAsync(file, "Module A\n    Sub Main()\n    End Sub\nEnd Module");

        using var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var editor = host.Window.GetVisualDescendants()
            .OfType<CodeEditor>().Single()
            .GetVisualDescendants().OfType<TextEditor>().Single();

        Assert.NotNull(editor.SyntaxHighlighting);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
