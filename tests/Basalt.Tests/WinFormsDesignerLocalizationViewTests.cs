using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// The Language path through the real designer view: the culture .resx is the editor buffer
/// MainForm.it.resx, edits land in that buffer, and the neutral buffer and the Designer.vb stay untouched.
/// </summary>
/// <remarks>
/// Defect reintroduced once to prove the test catches it: the culture commit was changed to update the
/// session without writing the culture buffer. The Italian buffer kept its old text and the test failed.
/// </remarks>
public sealed class WinFormsDesignerLocalizationViewTests
{
    [AvaloniaFact]
    public async Task LanguageEditsWriteOnlyTheCultureBufferAndUndoReachesIt()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb",
            WinFormsDesignerLocalizationDocumentTests.Localized, true);
        var neutral = new EditorDocumentViewModel("MainForm.resx", WinFormsDesignerLocalizationSessionTests.NeutralResources);
        var italian = new EditorDocumentViewModel("MainForm.it.resx", WinFormsDesignerLocalizationSessionTests.ItalianResources);
        using var view = new WinFormsDesignerView(designer, findCode: path => path switch
        {
            "MainForm.it.resx" => italian,
            "MainForm.resx" => neutral,
            _ => null,
        });
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            await WaitUntilAsync(() => view.DesignerSession is not null);

            await view.SetLanguageAsync("it");
            Assert.Equal("Pulsante", view.DesignerSession!.FindControl("Button1")!.Text);
            Assert.Equal("Form principale", view.DesignerSession.Form.Text);

            await view.DesignerSession.SetPropertyAsync(view.DesignerSession.FindControl("Button1")!, "Text", "Pulsante OK");

            Assert.Equal("Pulsante OK", new WinFormsResourceDocument(italian.Text).TryGet("Button1.Text")!.Value);
            Assert.Equal(WinFormsDesignerLocalizationSessionTests.NeutralResources, neutral.Text);
            Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, designer.Text);

            view.UndoDesignerEdit();
            await WaitUntilAsync(() => italian.Text == WinFormsDesignerLocalizationSessionTests.ItalianResources);
            Assert.Equal("Pulsante", view.DesignerSession.FindControl("Button1")!.Text);

            await view.SetLanguageAsync(null);
            Assert.Equal("Button", view.DesignerSession.FindControl("Button1")!.Text);
            Assert.Equal("Main form", view.DesignerSession.Form.Text);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The host's open documents as the app keeps them: findCode sees only what is open, and getCode reads a file
    /// from disk and opens it, the way GetWinFormsCodeAsync does.
    /// </summary>
    private sealed class OpenDocuments
    {
        public Dictionary<string, EditorDocumentViewModel> Open { get; } = new(StringComparer.Ordinal);

        public EditorDocumentViewModel? Find(string path) => Open.GetValueOrDefault(path);

        public async Task<EditorDocumentViewModel> GetAsync(string path)
        {
            if (Find(path) is { } existing) return existing;
            var document = new EditorDocumentViewModel(path, await File.ReadAllTextAsync(path));
            Open[path] = document;
            return document;
        }
    }

    /// <summary>A folder on disk for the form and its resource files, removed afterwards.</summary>
    private static async Task WithFolderAsync(Func<string, Task> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "basalt-localization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await test(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The culture files beside the neutral .resx are the ones the runtime names as cultures: MainForm.it.resx and
    /// MainForm.fr-FR.resx count, MainForm.backup.resx is the user's own file and does not.
    /// </summary>
    /// <remarks>
    /// Defect reintroduced once (every file named Form.*.resx was listed): the list held "backup", and a delete or rename
    /// would have changed its entries. The test failed on the listed names.
    /// </remarks>
    [AvaloniaFact]
    public async Task OnlyAFileNamedForARealCultureCountsAsACultureResx()
    {
        await WithFolderAsync(async folder =>
        {
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            await File.WriteAllTextAsync(Path.Combine(folder, "MainForm.resx"), WinFormsDesignerLocalizationSessionTests.NeutralResources);
            await File.WriteAllTextAsync(Path.Combine(folder, "MainForm.it.resx"), WinFormsDesignerLocalizationSessionTests.ItalianResources);
            await File.WriteAllTextAsync(Path.Combine(folder, "MainForm.fr-FR.resx"), WinFormsDesignerLocalizationSessionTests.ItalianResources);
            await File.WriteAllTextAsync(Path.Combine(folder, "MainForm.backup.resx"), WinFormsDesignerLocalizationSessionTests.ItalianResources);
            var designer = new EditorDocumentViewModel(designerPath, WinFormsDesignerLocalizationDocumentTests.Localized, true);
            var open = new OpenDocuments();
            using var view = new WinFormsDesignerView(designer, findCode: open.Find, getCode: open.GetAsync);
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                await WaitUntilAsync(() => view.DesignerSession is not null);

                var names = view.DesignerSession!.ListCultureResources!();

                Assert.Equal(["fr-FR", "it"], names.OrderBy(name => name, StringComparer.Ordinal));
            }
            finally { window.Close(); }
        });
    }

    /// <summary>
    /// Localizable = True on a form whose neutral .resx is on disk and not open: the switch opens it the way
    /// every other resource edit does, and the flag and the ApplyResources calls land in that buffer.
    /// </summary>
    /// <remarks>
    /// Defect reintroduced once (the session's toggle skipped PrepareResourceEdit): the commit found no open buffer,
    /// threw "The form code changed after this operation", and the test failed at SetLocalizableAsync.
    /// </remarks>
    [AvaloniaFact]
    public async Task LocalizableTrueOpensTheNeutralResxFromDiskWhenTheHostHasNotOpenedIt()
    {
        await WithFolderAsync(async folder =>
        {
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            var resxPath = Path.Combine(folder, "MainForm.resx");
            await File.WriteAllTextAsync(resxPath, WinFormsDesignerLocalizationSessionTests.EmptyResources);
            var designer = new EditorDocumentViewModel(designerPath,
                WinFormsDesignerLocalizationDocumentTests.NotLocalized, true);
            var open = new OpenDocuments();
            using var view = new WinFormsDesignerView(designer, findCode: open.Find, getCode: open.GetAsync);
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                await WaitUntilAsync(() => view.DesignerSession is not null);
                Assert.Null(open.Find(resxPath));

                await view.DesignerSession!.SetLocalizableAsync(true);

                var buffer = open.Find(resxPath);
                Assert.NotNull(buffer);
                Assert.Equal("True", new WinFormsResourceDocument(buffer.Text).TryGetMetadata("$this.Localizable")!.Value);
                Assert.Equal("Button", new WinFormsResourceDocument(buffer.Text).TryGet("Button1.Text")!.Value);
                Assert.Contains("resources.ApplyResources(Me, \"$this\")", designer.Text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// A culture file that exists but is not open is opened for the edit, not recreated: creating it would
    /// replace the translation with an empty resource.
    /// </summary>
    /// <remarks>
    /// Defect reintroduced once (EditableCultureBufferAsync created the culture file whenever no open buffer existed):
    /// the create hook replaced the file with an empty resource, the edit threw "The form code changed after this
    /// operation", and the test failed.
    /// </remarks>
    [AvaloniaFact]
    public async Task CultureEditOpensTheExistingCultureFileInsteadOfRecreatingIt()
    {
        await WithFolderAsync(async folder =>
        {
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            var neutralPath = Path.Combine(folder, "MainForm.resx");
            var italianPath = Path.Combine(folder, "MainForm.it.resx");
            await File.WriteAllTextAsync(neutralPath, WinFormsDesignerLocalizationSessionTests.NeutralResources);
            await File.WriteAllTextAsync(italianPath, WinFormsDesignerLocalizationSessionTests.ItalianResources);
            var designer = new EditorDocumentViewModel(designerPath,
                WinFormsDesignerLocalizationDocumentTests.Localized, true);
            var open = new OpenDocuments();
            var created = new List<string>();
            using var view = new WinFormsDesignerView(designer, findCode: open.Find, getCode: open.GetAsync,
                createCode: (path, text) =>
                {
                    created.Add(path);
                    return Task.FromResult(new EditorDocumentViewModel(path, text));
                });
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                await WaitUntilAsync(() => view.DesignerSession is not null);
                await view.SetLanguageAsync("it");
                // The user closed the Italian tab after choosing the language.
                open.Open.Remove(italianPath);

                var button = view.DesignerSession!.FindControl("Button1")!;
                await view.DesignerSession.SetPropertyAsync(button, "Text", "Pulsante OK");

                Assert.Empty(created);
                var reopened = open.Find(italianPath);
                Assert.NotNull(reopened);
                Assert.Equal("Pulsante OK", new WinFormsResourceDocument(reopened.Text).TryGet("Button1.Text")!.Value);
                Assert.Equal("Form principale", new WinFormsResourceDocument(reopened.Text).TryGet("$this.Text")!.Value);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// An Italian edit undone after the language moved to (Default) reaches MainForm.it.resx, the file it was
    /// made in, and never a file named by an empty culture ("MainForm..resx").
    /// </summary>
    /// <remarks>
    /// Defect reintroduced once (the prepare callback read the session's current culture instead of the one the edit
    /// captured): the undo named MainForm..resx, and the test failed at the wait for the Italian text.
    /// </remarks>
    [AvaloniaFact]
    public async Task UndoOfAnItalianEditAfterSwitchingToDefaultRestoresTheItalianFile()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb",
            WinFormsDesignerLocalizationDocumentTests.Localized, true);
        var neutral = new EditorDocumentViewModel("MainForm.resx", WinFormsDesignerLocalizationSessionTests.NeutralResources);
        var italian = new EditorDocumentViewModel("MainForm.it.resx", WinFormsDesignerLocalizationSessionTests.ItalianResources);
        var created = new List<string>();
        using var view = new WinFormsDesignerView(designer, findCode: path => path switch
        {
            "MainForm.it.resx" => italian,
            "MainForm.resx" => neutral,
            _ => null,
        }, createCode: (path, text) =>
        {
            created.Add(path);
            return Task.FromResult(new EditorDocumentViewModel(path, text));
        });
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            await WaitUntilAsync(() => view.DesignerSession is not null);
            await view.SetLanguageAsync("it");
            await view.DesignerSession!.SetPropertyAsync(view.DesignerSession.FindControl("Button1")!, "Text", "Pulsante OK");
            await view.SetLanguageAsync(null);

            view.UndoDesignerEdit();

            await WaitUntilAsync(() => italian.Text == WinFormsDesignerLocalizationSessionTests.ItalianResources);
            Assert.Empty(created);
            Assert.Equal(WinFormsDesignerLocalizationSessionTests.NeutralResources, neutral.Text);
        }
        finally { window.Close(); }
    }

    /// <remarks>
    /// Defect reintroduced once (the undo applied the edit without showing the language it was made in): the first
    /// Culture assertion failed with "Value is not null", because the undo ran with Italian on screen.
    /// </remarks>
    [AvaloniaFact]
    public async Task UndoAndRedoOfADefaultEditShowDefaultFirstAndLeaveTheItalianFileAlone()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb",
            WinFormsDesignerLocalizationDocumentTests.Localized, true);
        var neutral = new EditorDocumentViewModel("MainForm.resx", WinFormsDesignerLocalizationSessionTests.NeutralResources);
        var italian = new EditorDocumentViewModel("MainForm.it.resx", WinFormsDesignerLocalizationSessionTests.ItalianResources);
        using var view = new WinFormsDesignerView(designer, findCode: path => path switch
        {
            "MainForm.it.resx" => italian,
            "MainForm.resx" => neutral,
            _ => null,
        });
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            await WaitUntilAsync(() => view.DesignerSession is not null);
            var session = view.DesignerSession!;
            var designerBefore = designer.Text;
            await session.AddControlAsync(typeof(Forms.Button), "Me");
            var designerAdded = designer.Text;
            await view.SetLanguageAsync("it");
            var italianBefore = italian.Text;

            view.UndoDesignerEdit();

            await WaitUntilAsync(() => designer.Text == designerBefore);
            Assert.Null(session.Culture);
            Assert.Null(view.FormDesignRows()!.Culture);
            Assert.Null(session.FindControl("Button2"));
            Assert.Equal(italianBefore, italian.Text);

            await view.SetLanguageAsync("it");
            await view.RunEditForTestsAsync(() => session.RedoAsync());

            Assert.Equal(designerAdded, designer.Text);
            Assert.Null(session.Culture);
            Assert.Null(view.FormDesignRows()!.Culture);
            Assert.NotNull(session.FindControl("Button2"));
            Assert.Equal(italianBefore, italian.Text);
        }
        finally { window.Close(); }
    }

    /// <remarks>
    /// Defect reintroduced once (the refusal before the dialog removed from the property editor): the dialog opened in
    /// Italian, the status never showed the refusal, and the test failed at its wait with "The designer view never
    /// reached the expected state."
    /// </remarks>
    [AvaloniaFact]
    public async Task ImageEditorInALanguageIsRefusedBeforeItOpensTheDialogAndImportsNothing()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb",
            WinFormsDesignerLocalizationDocumentTests.Localized, true);
        var neutral = new EditorDocumentViewModel("MainForm.resx", WinFormsDesignerLocalizationSessionTests.NeutralResources);
        var italian = new EditorDocumentViewModel("MainForm.it.resx", WinFormsDesignerLocalizationSessionTests.ItalianResources);
        var imports = 0;
        using var view = new WinFormsDesignerView(designer, findCode: path => path switch
        {
            "MainForm.it.resx" => italian,
            "MainForm.resx" => neutral,
            _ => null,
        }, importImageFile: () =>
        {
            ++imports;
            return Task.FromResult<string?>(null);
        });
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            await WaitUntilAsync(() => view.DesignerSession is not null);
            await view.SetLanguageAsync("it");
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls.OfType<Forms.Button>().Single());
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => grid.Properties.Any(property => property.Name == "Image"));
            var row = Assert.Single(grid.Rows.OfType<Border>(), candidate => candidate.Name == "PropertyGridRow"
                && candidate.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Image"));
            var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "PropertyGridEllipsis");
            ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await WaitUntilAsync(() => view.GetVisualDescendants().OfType<TextBlock>()
                .Any(block => block.Text?.Contains(Localizer.Get(StringKeys.WinFormsDesignerNotInLanguage), StringComparison.Ordinal) == true));
            Assert.Empty(window.OwnedWindows.OfType<WinFormsImageResourceDialog>());
            Assert.Equal(0, imports);
            Assert.Equal(WinFormsDesignerLocalizationSessionTests.NeutralResources, neutral.Text);
            Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, designer.Text);
        }
        finally { window.Close(); }
    }

    /// <summary>The resx text a failed merge leaves behind: a conflict marker, which XML does not parse.</summary>
    private const string ConflictedResources = "<root>\n<<<<<<< HEAD\n</root>\n";

    /// <summary>A PNG signature and a few bytes: the image is stored as raw bytes, so it need not be a whole image.</summary>
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    /// <summary>The host's create hook as MainWindow runs it: the file is written to disk and opened in the editor.</summary>
    private static async Task<EditorDocumentViewModel> CreateOnDiskAsync(OpenDocuments open, string path, string text)
    {
        await File.WriteAllTextAsync(path, text);
        return open.Open[path] = new EditorDocumentViewModel(path, text);
    }

    /// <summary>
    /// A neutral .resx that exists but does not parse is neither replaced by the empty template nor read as empty:
    /// Localizable = True and a picture are refused, and the file keeps every byte.
    /// </summary>
    /// <remarks>
    /// Defect reintroduced once (the create hook without its existence check, and the host hook without its check):
    /// the session wrote the empty template over the conflicted file, so the first assertion failed because no
    /// refusal was thrown, and the byte comparison would have failed as well.
    /// </remarks>
    [AvaloniaFact]
    public async Task AConflictedNeutralResxIsRefusedAndNeverReplaced()
    {
        await WithFolderAsync(async folder =>
        {
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            var resxPath = Path.Combine(folder, "MainForm.resx");
            await File.WriteAllTextAsync(resxPath, ConflictedResources);
            var bytes = await File.ReadAllBytesAsync(resxPath);
            var designer = new EditorDocumentViewModel(designerPath,
                WinFormsDesignerLocalizationDocumentTests.NotLocalized, true);
            var open = new OpenDocuments();
            using var view = new WinFormsDesignerView(designer, findCode: open.Find, getCode: open.GetAsync,
                createCode: (path, text) => CreateOnDiskAsync(open, path, text));
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                await WaitUntilAsync(() => view.DesignerSession is not null);
                var session = view.DesignerSession!;
                var refusal = Localizer.Get(StringKeys.WinFormsDesignerResourcesNotReplaced);

                var localizable = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetLocalizableAsync(true));
                Assert.Equal(refusal, localizable.Message);
                var picture = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    session.SetImageResourcePropertyAsync(session.FindControl("Button1")!, "Image", PngBytes, isIcon: false));
                Assert.Equal(refusal, picture.Message);

                Assert.Equal(bytes, await File.ReadAllBytesAsync(resxPath));
                Assert.Equal(WinFormsDesignerLocalizationDocumentTests.NotLocalized, designer.Text);
                Assert.Null(open.Find(resxPath));
            }
            finally { window.Close(); }
        });
    }

    /// <summary>
    /// An edit that writes no value does not create the culture .resx: a culture value equal to the neutral one is a
    /// no-op, so the Italian file is not created on disk for it.
    /// </summary>
    /// <remarks>
    /// Defect reintroduced once (the culture buffer prepared before the no-op check in ApplyPropertyChangesAsync): the
    /// no-op created MainForm.it.resx from the empty template, and the test failed at its File.Exists assertion.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnEditThatWritesNoValueDoesNotCreateTheCultureResx()
    {
        await WithFolderAsync(async folder =>
        {
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            var neutralPath = Path.Combine(folder, "MainForm.resx");
            var italianPath = Path.Combine(folder, "MainForm.it.resx");
            await File.WriteAllTextAsync(neutralPath, WinFormsDesignerLocalizationSessionTests.NeutralResources);
            var designer = new EditorDocumentViewModel(designerPath,
                WinFormsDesignerLocalizationDocumentTests.Localized, true);
            var open = new OpenDocuments();
            var created = new List<string>();
            using var view = new WinFormsDesignerView(designer, findCode: open.Find, getCode: open.GetAsync,
                createCode: (path, text) =>
                {
                    created.Add(path);
                    return CreateOnDiskAsync(open, path, text);
                });
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                await WaitUntilAsync(() => view.DesignerSession is not null);
                await view.SetLanguageAsync("it");

                // "Button" is the neutral value, so in Italian this edit removes an override that does not exist.
                await view.DesignerSession!.SetPropertyAsync(view.DesignerSession.FindControl("Button1")!, "Text", "Button");

                Assert.Empty(created);
                Assert.False(File.Exists(italianPath));
                Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, designer.Text);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>
    /// An undo whose Italian file was deleted recreates it holding the value the undo restores, instead of throwing and
    /// leaving the edit on the undo stack.
    /// </summary>
    /// <remarks>
    /// Defect reintroduced once (the culture prepare created the missing file from the empty template): the undo threw
    /// 'The form code changed after this operation', the edit stayed on the stack, and the test failed at its wait for
    /// the restored Text.
    /// </remarks>
    [AvaloniaFact]
    public async Task UndoRecreatesADeletedCultureResxWithTheRestoredValue()
    {
        await WithFolderAsync(async folder =>
        {
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            var neutralPath = Path.Combine(folder, "MainForm.resx");
            var italianPath = Path.Combine(folder, "MainForm.it.resx");
            await File.WriteAllTextAsync(neutralPath, WinFormsDesignerLocalizationSessionTests.NeutralResources);
            var designer = new EditorDocumentViewModel(designerPath,
                WinFormsDesignerLocalizationDocumentTests.Localized, true);
            var open = new OpenDocuments();
            using var view = new WinFormsDesignerView(designer, findCode: open.Find, getCode: open.GetAsync,
                createCode: (path, text) => CreateOnDiskAsync(open, path, text));
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                await WaitUntilAsync(() => view.DesignerSession is not null);
                var session = view.DesignerSession!;
                await view.SetLanguageAsync("it");
                await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Pulsante");
                // Each edit rebuilds the preview, so the control is looked up again after it.
                await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Pulsante OK");
                Assert.True(File.Exists(italianPath));

                // The Italian file is deleted and its editor tab closed; the undo then restores the earlier value.
                File.Delete(italianPath);
                open.Open.Remove(italianPath);
                view.UndoDesignerEdit();

                await WaitUntilAsync(() => session.FindControl("Button1")!.Text == "Pulsante");
                Assert.True(File.Exists(italianPath));
                var recreated = new WinFormsResourceDocument(await File.ReadAllTextAsync(italianPath));
                Assert.Equal("Pulsante", recreated.TryGet("Button1.Text")!.Value);
                Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, designer.Text);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>
    /// Deleting a control in a Localizable form through the real view removes its entries from the Italian file the host
    /// holds, which the view lists beside the neutral .resx on disk; undo writes them back into that buffer.
    /// </summary>
    /// <remarks>
    /// Defect reintroduced once (the view's ListCultureResources binding removed): the session found no culture file to
    /// rewrite, MainForm.it.resx kept Button1.Text, and the first assertion failed.
    /// </remarks>
    [AvaloniaFact]
    public async Task DeletingAControlRemovesItsEntriesFromTheItalianFileTheHostHolds()
    {
        await WithFolderAsync(async folder =>
        {
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            var neutralPath = Path.Combine(folder, "MainForm.resx");
            var italianPath = Path.Combine(folder, "MainForm.it.resx");
            await File.WriteAllTextAsync(neutralPath, WinFormsDesignerLocalizationSessionTests.NeutralResources);
            await File.WriteAllTextAsync(italianPath, WinFormsDesignerLocalizationSessionTests.ItalianResources);
            var designer = new EditorDocumentViewModel(designerPath,
                WinFormsDesignerLocalizationDocumentTests.Localized, true);
            var open = new OpenDocuments();
            using var view = new WinFormsDesignerView(designer, findCode: open.Find, getCode: open.GetAsync,
                createCode: (path, text) => CreateOnDiskAsync(open, path, text));
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                await WaitUntilAsync(() => view.DesignerSession is not null);
                var session = view.DesignerSession!;

                await session.DeleteAsync(session.FindControl("Button1")!);

                var italian = open.Open[italianPath];
                Assert.Equal(["$this.Text"], new WinFormsResourceDocument(italian.Text).Entries.Select(entry => entry.Name));
                Assert.Equal(["$this.Text"], new WinFormsResourceDocument(open.Open[neutralPath].Text).Entries.Select(entry => entry.Name));
                Assert.DoesNotContain("Button1", designer.Text);

                view.UndoDesignerEdit();
                await WaitUntilAsync(() => italian.Text == WinFormsDesignerLocalizationSessionTests.ItalianResources);
                Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, designer.Text);
            }
            finally { window.Close(); }
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The designer view never reached the expected state.");
    }
}
