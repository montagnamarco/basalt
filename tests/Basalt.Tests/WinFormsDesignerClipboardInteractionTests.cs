using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Groups every test that touches <see cref="WinFormsDesignerView"/>'s static clipboard
/// so xUnit never runs one concurrently with another: both read and write
/// process-wide state (the clipboard payload and its generation counter) that a
/// parallel run from an unrelated collection could otherwise observe half-written.
/// </summary>
[CollectionDefinition(nameof(WinFormsDesignerClipboardCollection), DisableParallelization = true)]
public sealed class WinFormsDesignerClipboardCollection;

[Collection(nameof(WinFormsDesignerClipboardCollection))]
public sealed class WinFormsDesignerClipboardInteractionTests : IDisposable
{
    public WinFormsDesignerClipboardInteractionTests() => WinFormsDesignerView.ResetClipboardForTests();

    public void Dispose() => WinFormsDesignerView.ResetClipboardForTests();

    [AvaloniaFact]
    public async Task KeyboardCopyAndPasteDuplicatesNestedControlsWithOneUndo()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0]);
            SendKey(surface, Key.C);
            await WaitUntilAsync(() => view.IsEnabled && surface.CanPasteControls?.Invoke() == true);
            surface.Select(surface.RootForm);
            SendKey(surface, Key.V);
            await WaitUntilAsync(() => surface.RootForm!.Controls.Count == 2);
            var pasted = surface.SelectedControl!;
            Assert.NotEqual("Panel1", pasted.Name);
            Assert.Single(pasted.Controls);
            Assert.NotEqual("Button1", pasted.Controls[0].Name);
            Assert.Equal("Before", pasted.Controls[0].Text);
            var undo = view.GetVisualDescendants().OfType<Button>()
                .First(button => Equals(button.Content, Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == WinFormsDesignerDocumentTests.Source);
            Assert.Single(surface.RootForm!.Controls);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CopyInAnotherDesignerDuringAnAwaitedCutWinsTheSharedClipboard()
    {
        var documentA = new EditorDocumentViewModel("FormA.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        using var viewA = new WinFormsDesignerView(documentA);
        var windowA = new Window { Content = viewA, Width = 1100, Height = 700 };

        var documentB = new EditorDocumentViewModel("FormB.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        using var viewB = new WinFormsDesignerView(documentB);
        var windowB = new Window { Content = viewB, Width = 1100, Height = 700 };

        windowA.Show();
        windowB.Show();
        try
        {
            var surfaceA = Assert.Single(viewA.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            var surfaceB = Assert.Single(viewB.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surfaceA.RootForm is not null);
            await WaitUntilAsync(() => surfaceB.RootForm is not null);

            // A cuts the whole Panel1 subtree. The gate pauses CopySelectionAsync right
            // after it stamps the shared clipboard generation and before the delete that
            // its Cut awaits, so B's Copy below is guaranteed to run to completion first
            // rather than merely likely to on this machine.
            surfaceA.Select(surfaceA.RootForm!.Controls[0]);
            var gateEntered = new TaskCompletionSource();
            var releaseGate = new TaskCompletionSource();
            WinFormsDesignerView.ClipboardRaceGateForTests = async () =>
            {
                gateEntered.TrySetResult();
                await releaseGate.Task;
            };
            var cutTask = viewA.CopySelectionForTestsAsync(cut: true);
            await gateEntered.Task;

            // B copies the nested Button1 while A's Cut is still awaiting its delete.
            var buttonInB = surfaceB.RootForm!.Controls[0].Controls[0];
            surfaceB.Select(buttonInB);
            await viewB.CopySelectionForTestsAsync(cut: false);

            releaseGate.TrySetResult();
            await cutTask;

            Assert.Equal(["Button1"], WinFormsDesignerView.ClipboardForTests?.TopLevelNames);
        }
        finally
        {
            WinFormsDesignerView.ClipboardRaceGateForTests = null;
            windowA.Close();
            windowB.Close();
        }
    }

    private static void SendKey(Control control, Key key)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = KeyModifiers.Control,
        };
        control.RaiseEvent(args);
        Assert.True(args.Handled);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition());
    }
}
