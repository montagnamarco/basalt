using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;
using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class WinFormsDesignerZOrderTests
{
    private const string Source = """
        Partial Class Form1
            Private Sub InitializeComponent()
                Me.First = New System.Windows.Forms.Button()
                Me.Second = New System.Windows.Forms.Button()
                Me.Third = New System.Windows.Forms.Button()
                Me.Controls.Add(Me.Second)
                Me.Controls.Add(Me.First)
                Me.Controls.Add(Me.Third)
                ' Preserve this comment and unknown application expression.
                Me.First.Tag = DangerousCall()
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents First As System.Windows.Forms.Button
            Friend WithEvents Second As System.Windows.Forms.Button
            Friend WithEvents Third As System.Windows.Forms.Button
        End Class
        """;

    [Fact]
    public void ReadsCollectionOrderAndPersistsGroupOrderWithoutRewritingOtherCode()
    {
        var document = new WinFormsDesignerDocument(Source);
        Assert.Equal(new[] { "Second", "First", "Third" }, document.ZOrder["Me"]);
        var changed = document.SetZOrder(new[] { "Third", "First" }, true);
        var reopened = new WinFormsDesignerDocument(changed);
        Assert.Equal(new[] { "First", "Third", "Second" }, reopened.ZOrder["Me"]);
        Assert.Contains("' Preserve this comment and unknown application expression.", changed);
        Assert.Contains("Me.First.Tag = DangerousCall()", changed);
        Assert.Equal(changed, reopened.SetZOrder(new[] { "First", "Third" }, true));
        Assert.Equal(new[] { "Second", "First", "Third" },
            new WinFormsDesignerDocument(reopened.SetZOrder(new[] { "First", "Third" }, false)).ZOrder["Me"]);
    }

    [AvaloniaFact]
    public async Task PreviewOrderSaveReopenAndSingleUndoRedoAgree()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(Source));
        Assert.Equal("Second", session.Form.Controls[0].Name);
        await session.SetZOrderAsync(new[] { session.FindControl("Third")! }, true);
        var changed = session.Document.Source;
        Assert.Equal("Third", session.Form.Controls[0].Name);
        using var reopened = new WinFormsDesignerSession(new WinFormsDesignerDocument(changed));
        Assert.Equal("Third", reopened.Form.Controls[0].Name);
        await session.UndoAsync();
        Assert.Equal(Source, session.Document.Source);
        Assert.Equal("Second", session.Form.Controls[0].Name);
        await session.RedoAsync();
        Assert.Equal(changed, session.Document.Source);
        Assert.Equal("Third", session.Form.Controls[0].Name);
    }

    [AvaloniaFact]
    public async Task ContextMenuChangesPersistedOrderAndUndoRestoresIt()
    {
        var document = new EditorDocumentViewModel("Form1.Designer.vb", Source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = view.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls.Cast<System.Windows.Forms.Control>().Single(control => control.Name == "Third"));
            surface.ContextMenu!.Open(surface);
            Dispatcher.UIThread.RunJobs();
            var action = surface.ContextMenu.Items.OfType<MenuItem>().Single(item =>
                Equals(item.Header, Localizer.Get("Designer_BringToFront")));
            Assert.True(action.IsEnabled);
            action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => document.Text.Contains("Controls.SetChildIndex(Me.Third, 0)", StringComparison.Ordinal));
            Assert.Equal("Third", surface.RootForm!.Controls[0].Name);
            var undo = view.GetVisualDescendants().OfType<Button>().Single(button =>
                Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == Source);
            Assert.Equal("Second", surface.RootForm!.Controls[0].Name);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task NestedAddRangePreservesParentAndReopensPersistedChildOrder()
    {
        var source = Source.Replace("Me.First = New System.Windows.Forms.Button()",
                "Me.Panel1 = New System.Windows.Forms.Panel()\n        Me.First = New System.Windows.Forms.Button()")
            .Replace("Me.Controls.Add(Me.Second)\n        Me.Controls.Add(Me.First)\n        Me.Controls.Add(Me.Third)",
                "Me.Controls.Add(Me.Panel1)\n        Me.Panel1.Controls.AddRange(New System.Windows.Forms.Control() {Me.Second, Me.First, Me.Third})")
            .Replace("Friend WithEvents First As", "Friend WithEvents Panel1 As System.Windows.Forms.Panel\n    Friend WithEvents First As");
        var parsed = await Task.Run(() => new WinFormsDesignerDocument(source));
        Assert.Equal("Panel1", parsed.Controls.Single(control => control.Name == "First").ParentName);
        Assert.Equal(new[] { "Second", "First", "Third" }, parsed.ZOrder["Panel1"]);
        using var session = new WinFormsDesignerSession(parsed);
        Assert.Equal("Second", session.FindControl("Panel1")!.Controls[0].Name);
        await session.SetZOrderAsync(new[] { session.FindControl("Third")! }, true);
        var changed = session.Document.Source;
        Assert.Contains("Me.Panel1.Controls.AddRange", changed);
        Assert.Contains("Me.Panel1.Controls.SetChildIndex(Me.Third, 0)", changed);
        using var reopened = new WinFormsDesignerSession(await Task.Run(() => new WinFormsDesignerDocument(changed)));
        Assert.Equal("Third", reopened.FindControl("Panel1")!.Controls[0].Name);
        Assert.Same(reopened.FindControl("Panel1"), reopened.FindControl("First")!.Parent);
        await session.UndoAsync();
        Assert.Equal(source, session.Document.Source);
        Assert.Equal("Second", session.FindControl("Panel1")!.Controls[0].Name);
    }

    [Fact]
    public void DoesNotExecuteUnknownChildIndexExpression()
    {
        var source = Source.Replace("Me.ResumeLayout(False)",
            "Me.Controls.SetChildIndex(Me.First, DangerousCall())\n        Me.ResumeLayout(False)");
        var document = new WinFormsDesignerDocument(source);
        Assert.Equal(source, document.Source);
        Assert.Throws<NotSupportedException>(() => document.SetZOrder(new[] { "First" }, true));
        Assert.Throws<ArgumentException>(() => new WinFormsDesignerDocument(Source).SetZOrder(new[] { "Me" }, true));
    }
}
