using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

public class WinFormsDesignerRenameViewTests
{
    private const string Companion = """
        Partial Class MainForm
            Private Sub SaveAction(sender As Object, e As System.EventArgs) Handles Button1.Click
                Dim Button1 As String = "Button1"
                Me.Button1.Text = Button1
            End Sub
        End Class
        """;

    [AvaloniaFact]
    public async Task NamePropertyRenamesSemanticReferencesAndRuntimeNameWithLinkedUndo()
    {
        using var fixture = new RenameFixture();
        await fixture.StartAsync();
        await fixture.RenameAsync();
        Assert.Equal(1, fixture.RenameRequests);
        Assert.Contains("Handles SaveButton.Click", fixture.Code.Text);
        Assert.Contains("Dim Button1 As String = \"Button1\"", fixture.Code.Text);
        Assert.Contains("Me.SaveButton.Text = Button1", fixture.Code.Text);
        Assert.Contains("Me.SaveButton.Name = \"SaveButton\"", fixture.Designer.Text);
        Assert.Contains("' keep this comment", fixture.Designer.Text);
        Assert.Contains("Private Sub SaveAction", fixture.Code.Text);
        Assert.Equal("SaveButton", fixture.Surface.SelectedControl!.Name);
        WinFormsCompilationAssert.AssertCompiles(fixture.Designer.Text + Environment.NewLine + fixture.Code.Text);
        var after = fixture.Designer.Text;
        var codeAfter = fixture.Code.Text;
        fixture.ClickHistory(StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => fixture.Designer.Text == fixture.Original);
        Assert.Equal(Companion, fixture.Code.Text);
        Assert.Equal("Button1", fixture.Surface.SelectedControl!.Name);
        fixture.ClickHistory(StringKeys.MenuEditRedo);
        await WaitUntilAsync(() => fixture.Designer.Text == after);
        Assert.Equal(codeAfter, fixture.Code.Text);
        Assert.Equal("SaveButton", fixture.Surface.SelectedControl!.Name);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoRejectsLaterBufferChangesWithoutPartialMutationOrLosingHistory(bool editDesigner)
    {
        using var fixture = new RenameFixture();
        await fixture.StartAsync();
        await fixture.RenameAsync();
        var renamedDesigner = fixture.Designer.Text;
        var renamedCode = fixture.Code.Text;
        var changed = editDesigner ? fixture.Designer : fixture.Code;
        changed.Text += Environment.NewLine + "' Later unsaved edit";
        var laterText = changed.Text;
        fixture.ClickHistory(StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => fixture.View.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text?.Contains(Localizer.Get("WinFormsDesigner_CodeChanged"), StringComparison.Ordinal) == true));
        Assert.Equal(laterText, changed.Text);
        Assert.Equal(editDesigner ? renamedCode : renamedDesigner, editDesigner ? fixture.Code.Text : fixture.Designer.Text);
        Assert.Equal("SaveButton", fixture.Surface.SelectedControl!.Name);
        changed.Text = editDesigner ? renamedDesigner : renamedCode;
        fixture.ClickHistory(StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => fixture.Designer.Text == fixture.Original);
        Assert.Equal(Companion, fixture.Code.Text);
    }

    /// <summary>
    /// Undo, redo and undo of a rename in a Localizable form, through the real view and the real Roslyn rename. The form's
    /// files are on disk, because the view lists the culture .resx files from the folder: Designer.vb, the companion, the
    /// neutral resx and the Italian resx.
    /// </summary>
    /// <remarks>
    /// Defect reintroduced once (the undo checked the designer against the plain Roslyn rename, which keeps the old key): the
    /// undo threw CodeChanged after the resx files had been rewritten, and the test failed on the first undo's Designer text.
    /// </remarks>
    [AvaloniaFact]
    public async Task RenamingALocalizableControlUndoesAndRedoesEveryFileIncludingTheItalianResx()
    {
        using var fixture = new LocalizedRenameFixture();
        await fixture.StartAsync();
        await fixture.RenameAsync();
        var renamed = fixture.Texts();
        Assert.Contains("resources.ApplyResources(Me.SaveButton, \"SaveButton\")", renamed.Designer);
        Assert.Contains("SaveButton.Text", renamed.Italian);

        fixture.ClickHistory(StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => fixture.Designer.Text == fixture.Original);
        Assert.Equal(Companion, fixture.Code.Text);
        Assert.Equal(WinFormsDesignerLocalizationSessionTests.NeutralResources, fixture.Neutral.Text);
        Assert.Equal(WinFormsDesignerLocalizationSessionTests.ItalianResources, fixture.Italian.Text);
        Assert.Equal("Button1", fixture.Surface.SelectedControl!.Name);

        fixture.ClickHistory(StringKeys.MenuEditRedo);
        await WaitUntilAsync(() => fixture.Designer.Text == renamed.Designer);
        Assert.Equal(renamed.Code, fixture.Code.Text);
        Assert.Equal(renamed.Neutral, fixture.Neutral.Text);
        Assert.Equal(renamed.Italian, fixture.Italian.Text);
        Assert.Equal("SaveButton", fixture.Surface.SelectedControl!.Name);

        fixture.ClickHistory(StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => fixture.Designer.Text == fixture.Original);
        Assert.Equal(WinFormsDesignerLocalizationSessionTests.NeutralResources, fixture.Neutral.Text);
        Assert.Equal(WinFormsDesignerLocalizationSessionTests.ItalianResources, fixture.Italian.Text);
    }

    /// <remarks>
    /// Defect reintroduced once (the undo wrote every resx file before the companion edit): the companion edit threw on the
    /// changed Form code, and the neutral and Italian resx had already been rewritten, so the test failed on the resx texts.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoOfALocalizableRenameRejectsALaterEditWithoutWritingAnyResxFile(bool editDesigner)
    {
        using var fixture = new LocalizedRenameFixture();
        await fixture.StartAsync();
        await fixture.RenameAsync();
        var renamed = fixture.Texts();
        var changed = editDesigner ? fixture.Designer : fixture.Code;
        changed.Text += Environment.NewLine + "' Later unsaved edit";
        var laterText = changed.Text;

        fixture.ClickHistory(StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => fixture.View.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text?.Contains(Localizer.Get("WinFormsDesigner_CodeChanged"), StringComparison.Ordinal) == true));

        Assert.Equal(laterText, changed.Text);
        Assert.Equal(editDesigner ? renamed.Code : renamed.Designer, editDesigner ? fixture.Code.Text : fixture.Designer.Text);
        Assert.Equal(renamed.Neutral, fixture.Neutral.Text);
        Assert.Equal(renamed.Italian, fixture.Italian.Text);
        Assert.Equal("SaveButton", fixture.Surface.SelectedControl!.Name);

        changed.Text = editDesigner ? renamed.Designer : renamed.Code;
        fixture.ClickHistory(StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => fixture.Designer.Text == fixture.Original);
        Assert.Equal(WinFormsDesignerLocalizationSessionTests.NeutralResources, fixture.Neutral.Text);
        Assert.Equal(WinFormsDesignerLocalizationSessionTests.ItalianResources, fixture.Italian.Text);
    }

    private sealed class LocalizedRenameFixture : IDisposable
    {
        private readonly string _folder = Directory.CreateTempSubdirectory("basalt-localized-rename-").FullName;
        private readonly Dictionary<string, EditorDocumentViewModel> _buffers = new(StringComparer.Ordinal);
        private readonly Window _window;

        public LocalizedRenameFixture()
        {
            Original = WinFormsDesignerLocalizationDocumentTests.Localized;
            Designer = new EditorDocumentViewModel(Path.Combine(_folder, "MainForm.Designer.vb"), Original, true);
            Code = new EditorDocumentViewModel(Path.Combine(_folder, "MainForm.vb"), Companion);
            Neutral = new EditorDocumentViewModel(Path.Combine(_folder, "MainForm.resx"),
                WinFormsDesignerLocalizationSessionTests.NeutralResources);
            Italian = new EditorDocumentViewModel(Path.Combine(_folder, "MainForm.it.resx"),
                WinFormsDesignerLocalizationSessionTests.ItalianResources);
            foreach (var buffer in new[] { Designer, Code, Neutral, Italian })
            {
                File.WriteAllText(buffer.FilePath, buffer.Text);
                if (!ReferenceEquals(buffer, Designer)) _buffers[buffer.FilePath] = buffer;
            }
            View = new WinFormsDesignerView(Designer, findCode: path => _buffers.GetValueOrDefault(path),
                getCode: path => Task.FromResult(_buffers[path]), rename: PreviewRenameAsync);
            _window = new Window { Content = View, Width = 1100, Height = 700 };
        }

        public string Original { get; }
        public EditorDocumentViewModel Designer { get; }
        public EditorDocumentViewModel Code { get; }
        public EditorDocumentViewModel Neutral { get; }
        public EditorDocumentViewModel Italian { get; }
        public WinFormsDesignerView View { get; }
        public WinFormsDesignSurface Surface { get; private set; } = null!;

        public (string Designer, string Code, string Neutral, string Italian) Texts() =>
            (Designer.Text, Code.Text, Neutral.Text, Italian.Text);

        private Task<RefactoringPreview> PreviewRenameAsync(string path, string source, int position, string name)
        {
            var companion = Code.Text;
            return Task.Run(async () =>
            {
                using var workspace = new AdhocWorkspace();
                var project = workspace.AddProject("RenameView", LanguageNames.VisualBasic)
                    .WithCompilationOptions(new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                    .Concat([typeof(System.Windows.Forms.Form).Assembly.Location, typeof(System.Drawing.Font).Assembly.Location]).Distinct();
                project = project.AddMetadataReferences(paths.Select(reference => MetadataReference.CreateFromFile(reference)));
                project = project.AddDocument("MainForm.Designer.vb", SourceText.From(source), filePath: path).Project;
                project = project.AddDocument("MainForm.vb", SourceText.From(companion), filePath: Code.FilePath).Project;
                return await new RenameRefactoring(project.Solution).PreviewAsync(path, position, name);
            });
        }

        public async Task StartAsync()
        {
            _window.Show();
            Surface = View.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => Surface.RootForm is not null);
            Surface.Select(Surface.RootForm!.Controls[0]);
            Assert.Equal("Button1", Surface.SelectedControl!.Name);
        }

        public async Task RenameAsync()
        {
            var panel = View.GetVisualDescendants().OfType<WinFormsPropertyPanel>().Single();
            var grid = panel.GetVisualDescendants().OfType<PropertyGrid>().Single();
            var row = Assert.Single(grid.Rows, row => row.GetVisualDescendants().OfType<TextBlock>()
                .Any(label => label.Text == Localizer.Get("PropertyGrid_Name")));
            var editor = Assert.Single(row.GetVisualDescendants().OfType<TextBox>());
            editor.Text = "SaveButton";
            editor.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            await WaitUntilAsync(() => Surface.SelectedControl?.Name == "SaveButton");
        }

        public void ClickHistory(string key) => View.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, Localizer.Get(key)))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Dispose()
        {
            View.Dispose();
            _window.Close();
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class RenameFixture : IDisposable
    {
        public string Original { get; } = WinFormsDesignerDocumentTests.Source
            .Replace("Me.Button1.Tag = DangerousCall()", "Me.Button1.Tag = Nothing", StringComparison.Ordinal);
        public EditorDocumentViewModel Designer { get; }
        public EditorDocumentViewModel Code { get; } = new("/tmp/MainForm.vb", Companion);
        public WinFormsDesignerView View { get; }
        public WinFormsDesignSurface Surface { get; private set; } = null!;
        public int RenameRequests { get; private set; }
        private readonly Window _window;

        public RenameFixture()
        {
            Designer = new("/tmp/MainForm.Designer.vb", Original, true);
            View = new WinFormsDesignerView(Designer, findCode: path => path == Code.FilePath ? Code : null,
                getCode: path => Task.FromResult(Code), rename: PreviewRenameAsync);
            _window = new Window { Content = View, Width = 1100, Height = 700 };
        }

        private Task<RefactoringPreview> PreviewRenameAsync(string path, string source, int position, string name)
        {
            RenameRequests++;
            var companion = Code.Text;
            return Task.Run(async () =>
            {
                using var workspace = new AdhocWorkspace();
                var project = workspace.AddProject("RenameView", LanguageNames.VisualBasic)
                    .WithCompilationOptions(new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                    .Concat([typeof(System.Windows.Forms.Form).Assembly.Location, typeof(System.Drawing.Font).Assembly.Location]).Distinct();
                project = project.AddMetadataReferences(paths.Select(reference => MetadataReference.CreateFromFile(reference)));
                project = project.AddDocument("MainForm.Designer.vb", SourceText.From(source), filePath: path).Project;
                project = project.AddDocument("MainForm.vb", SourceText.From(companion), filePath: Code.FilePath).Project;
                return await new RenameRefactoring(project.Solution).PreviewAsync(path, position, name);
            });
        }

        public async Task StartAsync()
        {
            _window.Show();
            Surface = View.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => Surface.RootForm is not null);
            Surface.Select(Surface.RootForm!.Controls[0].Controls[0]);
            Assert.Equal("Button1", Surface.SelectedControl!.Name);
        }

        public async Task RenameAsync()
        {
            var panel = View.GetVisualDescendants().OfType<WinFormsPropertyPanel>().Single();
            var grid = panel.GetVisualDescendants().OfType<PropertyGrid>().Single();
            var row = Assert.Single(grid.Rows, row => row.GetVisualDescendants().OfType<TextBlock>()
                .Any(label => label.Text == Localizer.Get("PropertyGrid_Name")));
            var editor = Assert.Single(row.GetVisualDescendants().OfType<TextBox>());
            editor.Text = "SaveButton";
            editor.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            await WaitUntilAsync(() => Surface.SelectedControl?.Name == "SaveButton");
        }

        public void ClickHistory(string key) => View.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, Localizer.Get(key)))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Dispose()
        {
            View.Dispose();
            _window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 500; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate()) return;
            await Task.Delay(10);
        }
        Assert.True(predicate());
    }
}
