using System.Drawing;
using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsDesignerSessionTests
{
    [AvaloniaFact]
    public void BuildsLiveControlsWithoutExecutingUnknownExpressions()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        var button = Assert.IsType<Forms.Button>(session.FindControl("Button1"));
        Assert.IsType<Forms.Panel>(button.Parent);
        Assert.Equal("Before", button.Text);
        Assert.Equal(new Rectangle(-5, 20, 90, 30), button.Bounds);
        Assert.Equal(new Size(640, 480), session.Form.ClientSize);
        Assert.Null(button.Tag);
        Assert.Contains(session.PreviewProblems, problem => problem.StartsWith("Button1.Tag", StringComparison.Ordinal));
        Assert.Contains("DangerousCall()", session.Document.Source);
    }

    [AvaloniaFact]
    public async Task PersistsPropertyAndBoundsEditsWithOneUndoPerOperation()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        var original = session.Document.Source;
        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "After \"quoted\"");
        Assert.Equal("After \"quoted\"", session.FindControl("Button1")!.Text);
        Assert.Contains("\"After \"\"quoted\"\"\"", session.Document.Source);
        var afterText = session.Document.Source;

        await session.SetBoundsAsync(session.FindControl("Button1")!, new Rectangle(40, 50, 120, 45));
        Assert.Equal(new Rectangle(40, 50, 120, 45), session.FindControl("Button1")!.Bounds);
        await session.UndoAsync();
        Assert.Equal(afterText, session.Document.Source);
        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
        await session.RedoAsync();
        Assert.Equal(afterText, session.Document.Source);
    }

    [AvaloniaFact]
    public async Task AddAndDeleteAreUndoableAndNewEditsDiscardRedo()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        await session.AddControlAsync(typeof(Forms.Label), "Panel1");
        Assert.IsType<Forms.Label>(session.FindControl("Label1"));
        Assert.Same(session.FindControl("Panel1"), session.FindControl("Label1")!.Parent);
        await session.DeleteAsync(session.FindControl("Label1")!);
        Assert.Null(session.FindControl("Label1"));
        await session.UndoAsync();
        Assert.IsType<Forms.Label>(session.FindControl("Label1"));
        await session.SetPropertyAsync(session.FindControl("Label1")!, "Text", "New branch");
        var branched = session.Document.Source;
        await session.RedoAsync();
        Assert.Equal(branched, session.Document.Source);
    }

    [AvaloniaFact]
    public async Task InvalidInputPreservesSourceAndLivePreview()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        var original = session.Document.Source;
        var button = session.FindControl("Button1")!;
        await Assert.ThrowsAnyAsync<Exception>(() => session.SetPropertyAsync(button, "Width", "invalid"));
        Assert.Equal(original, session.Document.Source);
        Assert.Same(button, session.FindControl("Button1"));
    }

    [AvaloniaFact]
    public async Task EditsUseFieldIdentityWhenRuntimeNameDiffers()
    {
        var source = WinFormsDesignerDocumentTests.Source.Replace("Me.Button1.Text =", "Me.Button1.Name = \"OtherName\"\n        Me.Button1.Text =", StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));
        var button = session.FindControl("Button1")!;
        Assert.Equal("OtherName", button.Name);
        Assert.Equal("Button1", session.NameOf(button));
        Assert.True(session.IsExplicit(button, "Text"));
        await session.SetPropertyAsync(button, "Text", "Changed");
        Assert.Equal("Changed", session.FindControl("Button1")!.Text);
        Assert.Contains("Me.Button1.Text = \"Changed\"", session.Document.Source);
    }

    [AvaloniaFact]
    public async Task GroupBoundsEditIsPersistedAndUndoneAsOneOperation()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("Button2", "Global.System.Windows.Forms.Button", "Panel1");
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));
        var first = session.FindControl("Button1")!;
        var second = session.FindControl("Button2")!;
        var firstBounds = first.Bounds;
        var secondBounds = second.Bounds;
        var changed = 0;
        session.Changed += () => changed++;
        await session.SetBoundsBatchAsync(
        [
            new(first, firstBounds, new Rectangle(40, 50, firstBounds.Width, firstBounds.Height)),
            new(second, secondBounds, new Rectangle(65, 50, secondBounds.Width, secondBounds.Height)),
        ]);
        Assert.Equal(1, changed);
        Assert.Equal(new Point(40, 50), session.FindControl("Button1")!.Location);
        Assert.Equal(new Point(65, 50), session.FindControl("Button2")!.Location);
        await session.UndoAsync();
        Assert.Equal(source, session.Document.Source);
        Assert.Equal(firstBounds, session.FindControl("Button1")!.Bounds);
        Assert.Equal(secondBounds, session.FindControl("Button2")!.Bounds);
        await session.RedoAsync();
        Assert.Equal(new Point(40, 50), session.FindControl("Button1")!.Location);
        Assert.Equal(new Point(65, 50), session.FindControl("Button2")!.Location);
    }

    [AvaloniaFact]
    public async Task CompanionCommitFailureLeavesDesignerAndEarlierUndoHistoryUntouched()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Earlier edit");
        var beforeDelete = session.Document.Source;
        var button = session.FindControl("Button1")!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.DeleteAsync(button,
            () => throw new InvalidOperationException("Companion changed")));
        Assert.Equal(beforeDelete, session.Document.Source);
        Assert.Same(button, session.FindControl("Button1"));
        await session.UndoAsync();
        Assert.Equal(WinFormsDesignerDocumentTests.Source, session.Document.Source);
    }

    [AvaloniaFact]
    public async Task FailedCompanionUndoAndRedoPreserveBothSourcesAndHistory()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source));
        var original = session.Document.Source;
        var companion = "Before";
        void Restore(string expected, string replacement)
        {
            if (companion != expected) throw new InvalidOperationException("Companion changed");
            companion = replacement;
        }
        await session.DeleteAsync(session.FindControl("Button1")!,
            () => Restore("Before", "After"), () => Restore("After", "Before"), () => Restore("Before", "After"));
        var deleted = session.Document.Source;
        companion = "Later user edit";
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.UndoAsync());
        Assert.Equal("Later user edit", companion);
        Assert.Equal(deleted, session.Document.Source);
        companion = "After";
        await session.UndoAsync();
        Assert.Equal("Before", companion);
        Assert.Equal(original, session.Document.Source);

        companion = "Another user edit";
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RedoAsync());
        Assert.Equal("Another user edit", companion);
        Assert.Equal(original, session.Document.Source);
        companion = "Before";
        await session.RedoAsync();
        Assert.Equal("After", companion);
        Assert.Equal(deleted, session.Document.Source);
    }
}
