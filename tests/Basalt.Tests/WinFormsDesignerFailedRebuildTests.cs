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
}
