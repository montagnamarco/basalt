using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Tests;

public class WinFormsDesignerLayoutPersistenceTests
{
    [AvaloniaFact]
    public async Task RootReferenceWritesMeAndRebuildsAgainstTheNewForm()
    {
        var original = WinFormsDesignerDocumentTests.Source;
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(original));

        await session.SetPropertyValueAsync(session.FindControl("Panel1")!, "Tag", session.Form);

        Assert.Contains("Me.Panel1.Tag = Me", session.Document.Source);
        Assert.DoesNotContain("Me.Me", session.Document.Source);
        Assert.Same(session.Form, session.FindControl("Panel1")!.Tag);

        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
    }

    [Fact]
    public void CyclicParentsAreRejectedBeforeBuildingLiveControls()
    {
        var source = WinFormsDesignerDocumentTests.Source.Replace("Me.Controls.Add(Me.Panel1)",
            "Me.Button1.Controls.Add(Me.Panel1)", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => new WinFormsDesignerDocument(source));
    }

    [AvaloniaFact]
    public async Task TabOrderBatchReopensAndUndoesAsOneTransaction()
    {
        var original = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("Button2", "Global.System.Windows.Forms.Button", "Panel1");
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(original));
        var first = session.FindControl("Button1")!;
        var second = session.FindControl("Button2")!;
        var changes = 0;
        session.Changed += () => changes++;
        await session.SetTabIndicesBatchAsync([new(first, first.TabIndex, 1), new(second, second.TabIndex, 0)]);
        Assert.Equal(1, changes);
        using var reopened = new WinFormsDesignerSession(new WinFormsDesignerDocument(session.Document.Source));
        Assert.Equal(1, reopened.FindControl("Button1")!.TabIndex);
        Assert.Equal(0, reopened.FindControl("Button2")!.TabIndex);
        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
        await session.RedoAsync();
        Assert.Equal(1, session.FindControl("Button1")!.TabIndex);
    }

    [AvaloniaFact]
    public void PreviewLinksControlsToDeclaredTrayComponents()
    {
        var document = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source);
        var source = document.AddComponent("Menu1", "Global.System.Windows.Forms.ContextMenuStrip");
        source = new WinFormsDesignerDocument(source).SetProperty("Button1", "ContextMenuStrip", "Me.Menu1");
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));
        Assert.Same(session.FindComponent("Menu1"), session.FindControl("Button1")!.ContextMenuStrip);
        Assert.DoesNotContain(session.PreviewProblems, problem => problem.StartsWith("Button1.ContextMenuStrip", StringComparison.Ordinal));
    }
}
