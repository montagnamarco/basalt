using System.Drawing;
using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Tests;

public class WinFormsDesignerNestedSessionTests
{
    [AvaloniaFact]
    public async Task PointChildEditWritesOneParentAssignmentAndUndoesToExactSource()
    {
        var original = WinFormsDesignerDocumentTests.Source;
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(original));
        var button = session.FindControl("Button1")!;
        var changes = 0;
        session.Changed += () => changes++;

        await session.SetNestedPropertyAsync(button, "Location", "X", "42");

        Assert.Equal(new Point(42, 20), session.FindControl("Button1")!.Location);
        Assert.Single(session.Document.Source.Split('\n'), line => line.Contains("Me.Button1.Location =", StringComparison.Ordinal));
        Assert.Contains("Me.Button1.Location = New System.Drawing.Point(42, 20)", session.Document.Source);
        Assert.Equal(1, changes);

        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
        Assert.Equal(new Point(-5, 20), session.FindControl("Button1")!.Location);
    }

    [AvaloniaFact]
    public async Task SizeWidthEditKeepsHeightAndInvalidInputLeavesSourceAndUndoHistoryUntouched()
    {
        var original = WinFormsDesignerDocumentTests.Source;
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(original));
        var button = session.FindControl("Button1")!;

        await Assert.ThrowsAsync<FormatException>(() => session.SetNestedPropertyAsync(button, "Size", "Width", "invalid"));
        Assert.Equal(original, session.Document.Source);
        Assert.Same(button, session.FindControl("Button1"));
        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);

        await session.SetNestedPropertyAsync(button, "Size", "Width", "120");
        Assert.Equal(new Size(120, 30), session.FindControl("Button1")!.Size);
        Assert.Single(session.Document.Source.Split('\n'), line => line.Contains("Me.Button1.Size =", StringComparison.Ordinal));
        Assert.Contains("Me.Button1.Size = New System.Drawing.Size(120, 30)", session.Document.Source);
        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
    }

    [AvaloniaFact]
    public async Task ResettingOnePointChildUsesConstructorDefaultAndPreservesTheOtherChild()
    {
        var original = WinFormsDesignerDocumentTests.Source;
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(original));
        await session.SetNestedPropertyAsync(session.FindControl("Button1")!, "Location", "Y", "73");

        await session.SetNestedPropertyAsync(session.FindControl("Button1")!, "Location", "X", null);

        Assert.Equal(new Point(0, 73), session.FindControl("Button1")!.Location);
        Assert.Contains("Me.Button1.Location = New System.Drawing.Point(0, 73)", session.Document.Source);
        await session.UndoAsync();
        Assert.Equal(new Point(-5, 73), session.FindControl("Button1")!.Location);
        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
    }

    [AvaloniaFact]
    public async Task FontChildEditSerializesBeforeDisposalAndBuildsAUsablePreview()
    {
        var original = WinFormsDesignerDocumentTests.Source;
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(original));
        var before = session.FindControl("Button1")!.Font;

        await session.SetNestedPropertyAsync(session.FindControl("Button1")!, "Font", "Size", "14");

        var after = session.FindControl("Button1")!.Font;
        Assert.Equal(14f, after.Size);
        Assert.Equal(before.Name, after.Name);
        Assert.Contains("Me.Button1.Font = New System.Drawing.Font(", session.Document.Source);
        Assert.DoesNotContain(session.PreviewProblems, problem => problem.Contains("Button1.Font", StringComparison.Ordinal));
        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
    }
}
