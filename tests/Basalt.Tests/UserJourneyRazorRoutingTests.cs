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
    private Task<NewSolutionResult> CreateProjectAsync() =>
        SolutionTemplates.CreateAsync(_root, "Journey", ProjectTemplate.RazorPages);

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
}
