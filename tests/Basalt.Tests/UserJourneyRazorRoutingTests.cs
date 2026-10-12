using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// A Razor view in Visual Basic answered by its own language, driven the way a
/// developer at the keyboard drives it: a real <see cref="MainWindow"/>, a
/// project made from the ASP.NET Core template the IDE offers, and real key
/// presses.
///
/// The shell asks the language that owns a file for its diagnostics,
/// completion and go-to-definition. For a .vbhtml that language is Razor, so
/// the answers are the ones the Razor providers give, already placed on the
/// template. Visual Basic files keep the compiler path they always had.
/// </summary>
public sealed class UserJourneyRazorRoutingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-journey-razor-routing-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Creates the project the way "New Project" does, so the .vbhtml wiring
    /// the template writes is what the journey runs on. The solution is named
    /// Journey, so the model is Journey.IndexModel and its Title is declared in
    /// Pages/IndexModel.vb.
    /// </summary>
    private Task<NewSolutionResult> CreateProjectAsync(ProjectTemplate template = ProjectTemplate.RazorPages) =>
        SolutionTemplates.CreateAsync(_root, "Journey", template);

    /// <summary>The completion list's entries, as the editor shows them.</summary>
    private static IReadOnlyList<string> CompletionTexts(CodeEditor code) =>
        code.CompletionWindowForTests is { } window
            ? [.. window.CompletionList.CompletionData.Select(entry => entry.Text)]
            : [];

    /// <summary>Types each character as the keyboard would, one key at a time.</summary>
    private static void TypeText(MainWindow window, string text)
    {
        foreach (var character in text)
            window.KeyTextInput(character.ToString());
    }

    private static string ProjectDirectory(NewSolutionResult created) =>
        Path.GetDirectoryName(created.ProjectPath)!;

    /// <summary>
    /// Replaces the template's view with the source a journey needs, so the
    /// journey controls every line number it asserts on.
    /// </summary>
    private static string WriteView(string path, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    private static TextEditor TextEditorFor(MainWindow window, string filePath)
    {
        var code = window.CodeEditorForTests(filePath);
        Assert.NotNull(code);
        return code!.GetVisualDescendants().OfType<TextEditor>().Single();
    }

    private static void PressKey(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 150)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }
        return condition();
    }

    // ---- journey: a typo in a view's VB expression ------------------------

    private const string ViewWithMemberName =
        """
        @ModelType Journey.IndexModel
        <!DOCTYPE html>
        <html>
        <body>
            <h1>@Model.Title</h1>
        </body>
        </html>

        """;

    /// <summary>
    /// A misspelt member typed into a view's expression is an error in the
    /// problems list for that view, on the template line where the author
    /// typed it. The diagnostics are recomputed after typing pauses, so the
    /// journey types the typo rather than opening a file that already has it.
    /// Before the routing the compiler saw only generated code, so the list
    /// for the view stayed empty.
    /// </summary>
    [AvaloniaFact]
    public async Task AMisspelledExpressionTypedInAViewIsAnErrorOnItsTemplateLine()
    {
        var created = await CreateProjectAsync();
        var viewPath = WriteView(Path.Combine(ProjectDirectory(created), "Pages", "Index.vbhtml"), ViewWithMemberName);

        using var host = new TestWindow(created.SolutionPath);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(viewPath);
        await host.SettleAsync(5);

        // The user puts the caret inside "Title" and types an "x": "Titxle".
        // That is on template line 5, the line the markup starts on.
        const int expectedLine = 5;

        var editor = TextEditorFor(w, viewPath);
        editor.CaretOffset = ViewWithMemberName.IndexOf("Title", StringComparison.Ordinal) + 3;
        editor.TextArea.Focus();
        w.KeyTextInput("x");

        Assert.True(await WaitUntilAsync(host, () => vm.Diagnostics.Any(d =>
                d.FilePath == viewPath && d.Severity == DiagnosticSeverity.Error)),
            "the misspelt member typed into the view was never reported as an error.");

        var reported = vm.Diagnostics.Where(d => d.FilePath == viewPath).ToList();
        Assert.Contains(reported, d => d.Severity == DiagnosticSeverity.Error && d.Line == expectedLine);
    }

    // ---- journey: completion inside an @Code block ------------------------

    private const string ViewWithCodeBlock =
        """
        @ModelType Journey.IndexModel
        <!DOCTYPE html>
        <html>
        <body>
        @Code
            Dim greeting As String = "hello"
            Dim echo = gre
            Dim title = Model.Ti
        End Code
            <p>@greeting</p>
        </body>
        </html>

        """;

    /// <summary>
    /// Ctrl+Space inside an @Code block offers the local declared above it.
    /// The Razor provider asks the compiler about the code it generates for
    /// the view, so the list is the one the build would see.
    /// </summary>
    [AvaloniaFact]
    public async Task CompletionInsideAViewCodeBlockOffersALocalDeclaredAbove()
    {
        var created = await CreateProjectAsync();
        var viewPath = WriteView(Path.Combine(ProjectDirectory(created), "Pages", "Index.vbhtml"), ViewWithCodeBlock);

        using var host = new TestWindow(created.SolutionPath);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(viewPath);
        await host.SettleAsync(5);

        var code = w.CodeEditorForTests(viewPath);
        Assert.NotNull(code);
        var editor = TextEditorFor(w, viewPath);

        editor.CaretOffset = ViewWithCodeBlock.IndexOf("gre\n", StringComparison.Ordinal) + "gre".Length;
        editor.TextArea.Focus();
        PressKey(w, Key.Space, RawInputModifiers.Control, PhysicalKey.Space);

        Assert.True(await WaitUntilAsync(host, () => code!.CompletionOpenForTests),
            "Ctrl+Space inside an @Code block did not open the completion list.");
        Assert.True(await WaitUntilAsync(host, () => code!.SelectedCompletionForTests == "greeting"),
            $"expected the local 'greeting' selected inside @Code, got '{code!.SelectedCompletionForTests}'.");

        // The view's model is named by @ModelType, not declared in the view,
        // so "Model" means nothing to the VB compiler applied to the template
        // text. Only the generated code, where Model is typed, offers Title.
        PressKey(w, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        await host.SettleAsync(2);

        editor.CaretOffset = ViewWithCodeBlock.IndexOf("Model.Ti", StringComparison.Ordinal) + "Model.Ti".Length;
        editor.TextArea.Focus();
        PressKey(w, Key.Space, RawInputModifiers.Control, PhysicalKey.Space);

        Assert.True(await WaitUntilAsync(host, () => code!.SelectedCompletionForTests == "Title"),
            $"expected the model member Title inside @Code, got '{code!.SelectedCompletionForTests}'.");
    }

    // ---- journey: F12 from a view's expression ------------------------------

    /// <summary>
    /// F12 on a model member used in a view goes to its declaration in the model's
    /// .vb file; F12 on Model, which exists only in the generated class, goes nowhere
    /// rather than into generated code the author never wrote.
    /// </summary>
    /// <remarks>
    /// Waits for the solution to load, as a user does before navigating. Before the
    /// solution loads the generated view is compiled on its own, where the model class
    /// is unknown: the answer was a jump into a "Scratch.vb" that does not exist,
    /// because the stand-alone scratch document was not named like the in-project one
    /// the navigation provider recognises. Covered by AViewBeforeTheSolutionLoads...
    /// </remarks>
    [AvaloniaFact]
    public async Task F12OnAModelMemberInAViewGoesToItsDeclarationAndOnModelGoesNowhere()
    {
        var created = await CreateProjectAsync();
        var viewPath = WriteView(Path.Combine(ProjectDirectory(created), "Pages", "Index.vbhtml"), ViewWithMemberName);
        using var host = new TestWindow(created.SolutionPath);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        Assert.True(await WaitUntilAsync(host, () => vm.StartupProject is not null && !vm.IsBusy, maxPasses: 600),
            "the solution never finished loading.");
        await vm.OpenFileAsync(viewPath);
        await host.SettleAsync(5);

        var title = await vm.GoToDefinitionAsync(viewPath, ViewWithMemberName,
            ViewWithMemberName.IndexOf("Title", StringComparison.Ordinal) + 2);
        Assert.NotNull(title);
        Assert.Equal("IndexModel.vb", Path.GetFileName(title!.Value.FilePath));

        var model = await vm.GoToDefinitionAsync(viewPath, ViewWithMemberName,
            ViewWithMemberName.IndexOf("Model.Title", StringComparison.Ordinal) + 2);
        Assert.Null(model);
    }

    /// <summary>
    /// Before any solution is open, F12 in a view never jumps into the scratch
    /// document the generated view is compiled in.
    /// </summary>
    /// <remarks>
    /// The stand-alone scratch was named "Scratch.vb", which IsGeneratedView did not
    /// recognise, so F12 on Model answered that file; with that name restored this fails.
    /// </remarks>
    [AvaloniaFact]
    public async Task AViewBeforeTheSolutionLoadsNeverJumpsIntoTheGeneratedScratch()
    {
        var viewPath = WriteView(Path.Combine(_root, "Lone", "Index.vbhtml"), ViewWithMemberName);
        using var host = new TestWindow();
        await host.SettleAsync(5);
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        var model = await vm.GoToDefinitionAsync(viewPath, ViewWithMemberName,
            ViewWithMemberName.IndexOf("Model.Title", StringComparison.Ordinal) + 2);

        Assert.True(model is null || !Path.GetFileName(model.Value.FilePath).Contains("Scratch", StringComparison.OrdinalIgnoreCase),
            $"F12 jumped into {model?.FilePath}.");
    }

    // ---- journey: a Blazor component's parameter ----------------------------

    private const string ComponentWithParameter =
        """
        <h1>@Title</h1>
        <p></p>

        @Code
            <Global.Microsoft.AspNetCore.Components.Parameter>
            Public Property Title As String = "Card"

            Private Sub Describe()
                Dim length = Title.Length
            End Sub
        End Code

        """;

    /// <summary>
    /// A component's [Parameter] is offered in its markup as the user types
    /// "@Ti": the component's own members answer the expression, as they do
    /// in a view's code.
    /// </summary>
    [AvaloniaFact]
    public async Task TypingAtTiInAComponentsMarkupOffersItsParameter()
    {
        var created = await CreateProjectAsync(ProjectTemplate.Blazor);
        var componentPath = WriteView(Path.Combine(ProjectDirectory(created), "Components", "Card.vbrazor"), ComponentWithParameter);
        using var host = new TestWindow(created.SolutionPath);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        Assert.True(await WaitUntilAsync(host, () => vm.StartupProject is not null && !vm.IsBusy, maxPasses: 600),
            "the solution never finished loading.");
        await vm.OpenFileAsync(componentPath);
        await host.SettleAsync(5);

        var code = w.CodeEditorForTests(componentPath);
        Assert.NotNull(code);
        var editor = TextEditorFor(w, componentPath);
        editor.CaretOffset = ComponentWithParameter.IndexOf("<p>", StringComparison.Ordinal) + "<p>".Length;
        editor.TextArea.Focus();
        TypeText(w, "@Ti");

        Assert.True(await WaitUntilAsync(host, () => CompletionTexts(code!).Contains("Title")),
            $"expected the parameter Title offered for @Ti, got [{string.Join(", ", CompletionTexts(code!))}].");
    }

    // ---- journey: hover over a model member in a view ------------------------

    /// <summary>
    /// Hovering Model.Title in a view shows the property's type, as hovering
    /// the same property in a .vb file does. Quick info is routed through the
    /// language that owns the file, like the other three answers.
    /// </summary>
    [AvaloniaFact]
    public async Task HoveringAModelMemberInAViewShowsItsType()
    {
        var created = await CreateProjectAsync();
        var viewPath = WriteView(Path.Combine(ProjectDirectory(created), "Pages", "Index.vbhtml"), ViewWithMemberName);
        using var host = new TestWindow(created.SolutionPath);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        Assert.True(await WaitUntilAsync(host, () => vm.StartupProject is not null && !vm.IsBusy, maxPasses: 600),
            "the solution never finished loading.");
        await vm.OpenFileAsync(viewPath);
        await host.SettleAsync(5);

        var offset = ViewWithMemberName.IndexOf("Model.Title", StringComparison.Ordinal) + "Model.".Length + 2;

        var description = await vm.DescribeSymbolAsync(viewPath, offset, ViewWithMemberName);
        Assert.NotNull(description);
        Assert.Contains("String", description!.PlainSignature, StringComparison.Ordinal);

        var code = w.CodeEditorForTests(viewPath);
        Assert.NotNull(code);
        Assert.True(await code!.ShowHoverAtAsync(offset),
            "hovering Model.Title in the view did not show a tooltip.");
    }

    // ---- journey: a dot typed after an expression in markup -----------------

    /// <summary>
    /// Typing "@Model." inside a paragraph offers the model's members at once,
    /// although the dot is still followed by markup.
    /// </summary>
    /// <remarks>
    /// "&lt;p&gt;@Model.&lt;/p&gt;" generates Write(Model): the trailing dot is markup to Razor,
    /// so the compiler was asked about "Model" and nothing was offered (0 items, while
    /// "@Model.Ti" and "Model." in a code block offered 57). VbHtmlCompletionProvider
    /// now puts the dot back into the code it asks about (WithTrailingDot); without
    /// it this test fails.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADotTypedAfterAnExpressionInMarkupOffersTheModelsMembers()
    {
        var created = await CreateProjectAsync();
        const string view = "@ModelType Journey.IndexModel\n<html>\n<body>\n<p>@Model.</p>\n</body>\n</html>\n";
        var viewPath = WriteView(Path.Combine(ProjectDirectory(created), "Pages", "Index.vbhtml"), view);
        using var host = new TestWindow(created.SolutionPath);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        Assert.True(await WaitUntilAsync(host, () => vm.StartupProject is not null && !vm.IsBusy, maxPasses: 600),
            "the solution never finished loading.");
        await vm.OpenFileAsync(viewPath);
        await host.SettleAsync(5);

        var position = view.IndexOf("<p>@Model.", StringComparison.Ordinal) + "<p>@Model.".Length;
        var items = await vm.GetCompletionsAsync(viewPath, position, view);

        Assert.Contains(items, item => item.DisplayText == "Title");
    }

    /// <summary>
    /// A sentence in a view's markup that ends on an expression: "Hi @Model.Title."
    /// The dot may start a member as well, so the list opens, but with nothing
    /// selected, and Enter then starts the next line instead of committing a member.
    /// </summary>
    /// <remarks>
    /// Defects reintroduced once, each on its own: (1) without IsTrailingDotInMarkup the
    /// list opened on the first member, and the selection assertion failed; (2) without
    /// the Enter pass-through in OnKeyDown the list swallowed Enter, and the text
    /// assertion failed with no new line.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADotEndingASentenceInMarkupOffersMembersWithoutCommittingOne()
    {
        var created = await CreateProjectAsync();
        const string view = "@ModelType Journey.IndexModel\n<html>\n<body>\n<p>Hi </p>\n</body>\n</html>\n";
        var viewPath = WriteView(Path.Combine(ProjectDirectory(created), "Pages", "Index.vbhtml"), view);
        using var host = new TestWindow(created.SolutionPath);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        Assert.True(await WaitUntilAsync(host, () => vm.StartupProject is not null && !vm.IsBusy, maxPasses: 600),
            "the solution never finished loading.");
        await vm.OpenFileAsync(viewPath);
        await host.SettleAsync(5);
        var code = w.CodeEditorForTests(viewPath)!;
        var editor = TextEditorFor(w, viewPath);
        editor.CaretOffset = view.IndexOf("Hi </p>", StringComparison.Ordinal) + "Hi ".Length;
        editor.TextArea.Focus();
        TypeText(w, "@Model.Title");
        await host.SettleAsync(5);
        PressKey(w, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        await host.SettleAsync(2);

        TypeText(w, ".");

        Assert.True(await WaitUntilAsync(host, () => code.CompletionOpenForTests && CompletionTexts(code).Count > 0),
            "the dot after @Model.Title offered no members.");
        Assert.Null(code.SelectedCompletionForTests);

        PressKey(w, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
        await host.SettleAsync(3);

        Assert.True(editor.Text.Contains("<p>Hi @Model.Title.\n", StringComparison.Ordinal),
            $"Enter after the sentence's dot did not start a new line: {editor.Text}");
        Assert.False(code.CompletionOpenForTests);
    }
}
