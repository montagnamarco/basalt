using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public class WinFormsDesignerStaleEditTests
{
    [AvaloniaFact]
    public async Task LinkedDeleteUndoUsesTheReopenedCompanionBuffer()
    {
        var originalDesigner = WinFormsDesignerDocumentTests.Source;
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", originalDesigner, true);
        var firstCode = new EditorDocumentViewModel("MainForm.vb", """
            Partial Class MainForm
                Private Sub CompanionHandler(sender As Object, e As System.EventArgs) Handles Button1.Click, Me.Load
                    System.Console.WriteLine("Keep the body")
                End Sub
            End Class
            """);
        var originalCode = firstCode.Text;
        EditorDocumentViewModel? activeCode = firstCode;
        using var view = new WinFormsDesignerView(designer,
            findCode: path => path == firstCode.FilePath ? activeCode : null,
            getCode: _ => Task.FromResult(activeCode!));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = view.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            await WaitUntilAsync(() => !designer.Text.Contains("Friend WithEvents Button1", StringComparison.Ordinal)
                && view.IsEnabled);
            var deletedDesigner = designer.Text;
            var deletedCode = firstCode.Text;
            Assert.DoesNotContain("Handles Button1.Click", deletedCode);

            // The first editor tab was closed and the same file was reopened from its latest text.
            activeCode = new EditorDocumentViewModel(firstCode.FilePath, deletedCode);
            var reopenedCode = activeCode;
            var field = typeof(WinFormsDesignerView).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var session = Assert.IsType<WinFormsDesignerSession>(field.GetValue(view));
            await session.UndoAsync();
            Assert.Equal(originalDesigner, designer.Text);
            Assert.Equal(originalCode, reopenedCode.Text);
            Assert.Equal(deletedCode, firstCode.Text);

            await session.RedoAsync();
            Assert.Equal(deletedDesigner, designer.Text);
            Assert.Equal(deletedCode, reopenedCode.Text);
            Assert.Equal(deletedCode, firstCode.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InFlightDesignerEditDoesNotOverwriteANewerCodeBuffer()
    {
        var original = WinFormsCollectionDocumentTests.Source;
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", original, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = view.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            var field = typeof(WinFormsDesignerView).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var session = Assert.IsType<WinFormsDesignerSession>(field.GetValue(view));
            var changed = 0;
            session.Changed += () => changed++;

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var edit = session.ApplyCollectionAsync(model =>
            {
                started.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release the designer edit.");
                return model.SetProperty("Me", "Text", "\"Designer edit\"");
            });

            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var externalEdit = original + Environment.NewLine + "' Edited in code while the designer was busy";
                document.Text = externalEdit;
                release.Set();

                // This task was started above by this test and is released before awaiting it.
#pragma warning disable VSTHRD003
                await Assert.ThrowsAsync<InvalidOperationException>(() => edit);
#pragma warning restore VSTHRD003
                Assert.Equal(externalEdit, document.Text);
                Assert.Equal(original, session.Document.Source);
                Assert.Equal(0, changed);
                await session.UndoAsync();
                Assert.Equal(original, session.Document.Source);
                Assert.Equal(externalEdit, document.Text);
            }
            finally
            {
                release.Set();
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 200; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate()) return;
            await Task.Delay(10);
        }
        Assert.True(predicate());
    }
}
