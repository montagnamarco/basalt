using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// A rebuild that throws partway. BuildPreview fills the session's form and control map as it goes, so a failure
/// used to leave the session on a half-built form the surface never showed.
/// </summary>
/// <remarks>
/// Defect reintroduced once to prove this test catches it: Replace assigned the new build without the rollback, so the
/// failed rebuild left the session's Form as the half-built instance. The test failed on that Form assertion.
/// </remarks>
public sealed class WinFormsDesignerFailedRebuildTests
{
    [Fact]
    public async Task AFailedRebuildKeepsTheShownFormAndItsControlsInPlace()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        var form = session.Form;
        var button = session.FindControl("Button1")!;
        var document = session.Document;
        var previewProblems = session.PreviewProblems;
        var changes = 0;
        session.Changed += () => changes++;

        // A top-level Form cannot be added to a control. The rebuild builds its controls in order and fails on the last
        // one, after Button1 was built and added to the new form.
        await Assert.ThrowsAsync<ArgumentException>(() => session.ApplyCollectionAsync(current =>
            current.AddControl("Form2", "Global.System.Windows.Forms.Form", "Me")));

        Assert.Same(document, session.Document);
        Assert.Same(form, session.Form);
        Assert.False(form.IsDisposed);
        Assert.Same(button, session.FindControl("Button1"));
        Assert.Null(session.FindControl("Form2"));
        Assert.Same(form, button.TopLevelControl);
        Assert.Equal(previewProblems, session.PreviewProblems);
        Assert.Equal(0, changes);
    }

    /// <remarks>
    /// Defect reintroduced once: ApplyAsync pushed the undo entry before Replace, so the failed rebuild left its entry
    /// on the undo stack. The test failed on the undo depth (2 instead of 1), and Undo would have reverted an edit that
    /// never reached the document.
    /// </remarks>
    [Fact]
    public async Task AFailedEditAddsNoUndoStepAndUndoRevertsOnlyTheLastEditThatHappened()
    {
        var original = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source);
        using var session = new WinFormsDesignerSession(original);
        await session.AddControlAsync(typeof(Forms.Button), "Me");
        var afterAdd = session.Document.Source;
        Assert.Equal(1, session.UndoDepth);

        await Assert.ThrowsAsync<ArgumentException>(() => session.ApplyCollectionAsync(current =>
            current.AddControl("Form2", "Global.System.Windows.Forms.Form", "Me")));

        Assert.Equal(1, session.UndoDepth);
        Assert.Equal(afterAdd, session.Document.Source);
        Assert.NotNull(session.FindControl("Button2"));

        await session.UndoAsync();

        Assert.Equal(original.Source, session.Document.Source);
        Assert.Null(session.FindControl("Button2"));
        Assert.Equal(0, session.UndoDepth);
    }

    [Fact]
    public async Task TheSessionKeepsEditingAfterAFailedRebuild()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        await Assert.ThrowsAsync<ArgumentException>(() => session.ApplyCollectionAsync(current =>
            current.AddControl("Form2", "Global.System.Windows.Forms.Form", "Me")));

        await session.AddControlAsync(typeof(Forms.Button), "Me");

        var added = session.FindControl("Button2");
        Assert.NotNull(added);
        Assert.Same(session.Form, added!.TopLevelControl);
        Assert.Null(session.FindControl("Form2"));
    }

    /// <summary>
    /// A project control whose type lives in an output folder the test deletes before the edit. Every rebuild looks the type
    /// up in that folder, so each rebuild after the deletion throws IOException: a rebuild that fails on purpose, after the
    /// edit has written its resource files.
    /// </summary>
    private sealed class DeletedOutputFolder : IDisposable
    {
        public DeletedOutputFolder()
        {
            Folder = Directory.CreateTempSubdirectory("basalt-failed-rebuild-").FullName;
            Context = new WinFormsDesignAssemblyContext(Path.Combine(Folder, "App.dll"));
        }

        public WinFormsDesignAssemblyContext Context { get; }

        private string Folder { get; }

        public void Remove() => Directory.Delete(Folder, recursive: true);

        public void Dispose()
        {
            Context.Unload();
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
    }

    /// <summary>A resx with no entries, as a non-localizable form's resource file starts out. Explicit "\n" line ends.</summary>
    private static readonly string EmptyResx = string.Join("\n",
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
        "<root>",
        "  <resheader name=\"resmimetype\">text/microsoft-resx</resheader>",
        "  <resheader name=\"version\">2.0</resheader>",
        "</root>");

    /// <remarks>
    /// Defect reintroduced once (ApplyAsync ran the rebuild with no revert, so the companion that wrote the picture's entry
    /// was not undone). Red: the first resx assertion failed, Form1.resx still held PictureBox1's data entry after the
    /// failed paste. Green with the rollback in place.
    /// </remarks>
    [Fact]
    public async Task AFailedRebuildOfAPicturePasteLeavesTheResxAndTheUndoStackAsTheyWere()
    {
        using var source = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizedClipboardTests.LocalizedWithPicture),
            new WinFormsResourceDocument(WinFormsDesignerLocalizedClipboardTests.NeutralWithPicture));
        var clipboard = source.CopyControls([source.FindControl("PictureBox1")!]);
        using var output = new DeletedOutputFolder();
        using var target = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized),
            new WinFormsResourceDocument(EmptyResx), designContext: output.Context);
        await target.ApplyCollectionAsync(document => document.AddControl("Thing1", "Global.App.Thing", "Me"));
        // The host's resx buffer: every write must start from the text the buffer holds, as the designer's host does.
        var buffer = EmptyResx;
        target.CommitResourceSource = (expected, replacement) =>
        {
            if (buffer != expected) throw new InvalidOperationException("The resx buffer changed.");
            buffer = replacement;
        };
        var document = target.Document;
        var undoDepth = target.UndoDepth;
        output.Remove();

        await Assert.ThrowsAnyAsync<IOException>(() => target.PasteControlsAsync(clipboard, target.Form));

        Assert.Equal(EmptyResx, target.ResourceSource);
        Assert.Equal(EmptyResx, buffer);
        Assert.Same(document, target.Document);
        Assert.Null(target.FindControl("PictureBox1"));
        Assert.Equal(undoDepth, target.UndoDepth);
    }

    /// <remarks>
    /// Defect reintroduced once (the companion revert dropped). Red: the neutral resx assertion failed, the session's neutral
    /// text still held Button2.Location after the failed add. Green with the rollback in place.
    /// </remarks>
    [Fact]
    public async Task AFailedRebuildOfALocalizableAddLeavesTheNeutralAndItalianResxAsTheyWere()
    {
        using var output = new DeletedOutputFolder();
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(WinFormsDesignerLocalizationSessionTests.NeutralResources), designContext: output.Context);
        await session.ApplyCollectionAsync(document => document.AddControl("Thing1", "Global.App.Thing", "Me"));
        var italian = WinFormsDesignerLocalizationSessionTests.ItalianResources;
        var neutralBuffer = WinFormsDesignerLocalizationSessionTests.NeutralResources;
        var italianBuffer = italian;
        var cultureWrites = 0;
        session.CommitResourceSource = (expected, replacement) =>
        {
            if (neutralBuffer != expected) throw new InvalidOperationException("The neutral resx buffer changed.");
            neutralBuffer = replacement;
        };
        session.ListCultureResources = () => ["it"];
        session.LoadCultureResources = culture => Task.FromResult<WinFormsResourceDocument?>(
            culture == "it" ? new WinFormsResourceDocument(italianBuffer) : null);
        session.CommitCultureResourceSource = (_, _, replacement) =>
        {
            ++cultureWrites;
            italianBuffer = replacement;
        };
        var document = session.Document;
        var neutralBefore = session.ResourceSource;
        var undoDepth = session.UndoDepth;
        output.Remove();

        await Assert.ThrowsAnyAsync<IOException>(() => session.AddControlAsync(typeof(Forms.Button), "Me"));

        Assert.Same(document, session.Document);
        Assert.Equal(neutralBefore, session.ResourceSource);
        Assert.Equal(WinFormsDesignerLocalizationSessionTests.NeutralResources, neutralBuffer);
        Assert.Equal(italian, italianBuffer);
        Assert.Equal(0, cultureWrites);
        Assert.Null(session.FindControl("Button2"));
        Assert.Equal(undoDepth, session.UndoDepth);
    }

    /// <remarks>
    /// Defect reintroduced once (the companion revert dropped, as above). Red: the neutral resx assertion failed, Button1's
    /// entries were still missing from the neutral file after the failed delete. A delete writes the neutral and the Italian
    /// resx, and the rebuild then fails on the project control, so both files must be put back.
    /// </remarks>
    [Fact]
    public async Task AFailedRebuildOfALocalizableDeleteLeavesTheNeutralAndItalianResxAsTheyWere()
    {
        using var output = new DeletedOutputFolder();
        // Button1 has entries in both files, so deleting it rewrites the neutral and the Italian resx. Thing1 is a project
        // control: it is not built while the folder exists, but every rebuild after the folder is gone looks it up and throws.
        var neutral = WinFormsDesignerLocalizationSessionTests.NeutralResources;
        var italian = WinFormsDesignerLocalizationSessionTests.ItalianResources;
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(neutral), designContext: output.Context);
        await session.ApplyCollectionAsync(document => document.AddControl("Thing1", "Global.App.Thing", "Me"));
        var neutralBuffer = neutral;
        var italianBuffer = italian;
        session.CommitResourceSource = (expected, replacement) =>
        {
            if (neutralBuffer != expected) throw new InvalidOperationException("The neutral resx buffer changed.");
            neutralBuffer = replacement;
        };
        session.ListCultureResources = () => ["it"];
        session.LoadCultureResources = culture => Task.FromResult<WinFormsResourceDocument?>(
            culture == "it" ? new WinFormsResourceDocument(italianBuffer) : null);
        session.CommitCultureResourceSource = (_, expected, replacement) =>
        {
            if (italianBuffer != expected) throw new InvalidOperationException("The Italian resx buffer changed.");
            italianBuffer = replacement;
        };
        var document = session.Document;
        var neutralBefore = session.ResourceSource;
        var undoDepth = session.UndoDepth;
        output.Remove();

        await Assert.ThrowsAnyAsync<IOException>(() => session.DeleteAsync(session.FindControl("Button1")!));

        Assert.Same(document, session.Document);
        Assert.Equal(neutralBefore, session.ResourceSource);
        Assert.Equal(neutral, neutralBuffer);
        Assert.Equal(italian, italianBuffer);
        Assert.NotNull(session.FindControl("Button1"));
        Assert.Equal(undoDepth, session.UndoDepth);
    }
}
