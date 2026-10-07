using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Where the completion popup lands, in a real window at a known offset.
/// </summary>
/// <remarks>
/// <see cref="ScreenshotProbe"/>'s completion.png once showed the list pinned
/// to the window's top-left corner instead of under "Me.". That turned out to
/// be the probe's own fault (its one-shot render never pumped the dispatcher,
/// so AvaloniaEdit's lazily-computed popup position never got applied before
/// the bitmap was captured) rather than a defect in <see cref="CodeEditor"/>
/// itself: these tests prove the real popup's position directly, against the
/// caret's own visual position, the way AvaloniaEdit computes it
/// (<c>TextView.GetVisualPosition</c> translated to the window), with no
/// screenshot involved.
/// </remarks>
public sealed class CompletionPopupPlacementTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-completion-placement-").FullName;
    private MainWindowViewModel _shell = null!;

    // An instance method, so "Me." resolves to something: Work() below, on
    // the same class, gives the list the usual member names to offer.
    private const string Source = """
        Class Form1
            Private Sub Form1_Load()
                Dim title = Me.Text
            End Sub

            Public Property Text As String
        End Class

        """;

    public async ValueTask InitializeAsync()
    {
        var project = Path.Combine(_root, "Placement.vbproj");
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(_root, "Form1.vb"), Source);
        _shell = new MainWindowViewModel();
        await _shell.OpenSolutionAsync(project);
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Opens the file in a window of a known size, at a caret right after
    /// "Me." on the "Dim title" line.
    /// </summary>
    private (Window Window, CodeEditor Code, TextEditor Editor, int Caret) Open()
    {
        var code = new CodeEditor(new EditorDocumentViewModel(Path.Combine(_root, "Form1.vb"), Source), _shell)
        {
            AutoFormatWhileTyping = false
        };

        // A window with room on every side of the caret: a popup pinned to
        // (0,0) and one placed correctly would both still fit on screen,
        // which is what makes this a real position assertion rather than a
        // "does it open off-screen" one.
        var window = new Window { Content = code, Width = 900, Height = 600 };
        window.Show();
        window.UpdateLayout();

        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
        var caret = Source.IndexOf("Me.Text", StringComparison.Ordinal) + "Me.".Length;
        editor.CaretOffset = caret;
        editor.TextArea.Focus();

        return (window, code, editor, caret);
    }

    /// <summary>
    /// The caret's own visual position, translated to the window the same
    /// way AvaloniaEdit's <c>CompletionWindowBase</c> places its popup: the
    /// bottom of the caret's line, in <see cref="TextView"/> coordinates,
    /// moved to window coordinates.
    /// </summary>
    private static Point CaretBottomInWindow(TextEditor editor, Window window, int caret)
    {
        var textView = editor.TextArea.TextView;
        var line = editor.Document.GetLineByOffset(caret);
        var column = caret - line.Offset + 1;
        var position = new TextViewPosition(line.LineNumber, column);

        var visual = textView.GetVisualPosition(position, VisualYPosition.LineBottom);

        return textView.TranslatePoint(visual - textView.ScrollOffset, window)!.Value;
    }

    [AvaloniaFact]
    public async Task OpensJustBelowTheCaretAfterMeDot()
    {
        var (window, code, editor, caret) = Open();
        try
        {
            await code.ShowCompletionForTestsAsync();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!code.CompletionOpenForTests && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }

            Assert.True(code.CompletionOpenForTests, "the completion list did not open after \"Me.\".");

            // The pump a running IDE does every frame and a one-shot render
            // does not: without it the popup is still at the (0,0) it opened
            // with, which is the defect this test exists to catch.
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var popup = code.CompletionWindowForTests!;
            var expected = CaretBottomInWindow(editor, window, caret);

            var overlayHost = window.GetVisualDescendants()
                .OfType<Control>()
                .Single(c => c.GetType().Name == "OverlayPopupHost");

            Assert.True(popup.IsOpen);

            // A few pixels: AvaloniaEdit nudges the popup clear of the caret
            // glyph itself, so this is not pixel-exact, but it must land
            // under the caret, not at the window's corner.
            Assert.InRange(overlayHost.Bounds.X, expected.X - 5, expected.X + 5);
            Assert.InRange(overlayHost.Bounds.Y, expected.Y - 5, expected.Y + 5);

            // The regression this guards against: the window's corner, which
            // is nowhere near a caret sitting several lines into the method.
            Assert.False(overlayHost.Bounds.X < 5 && overlayHost.Bounds.Y < 5,
                "the completion popup is pinned to the window's corner instead of the caret.");
        }
        finally
        {
            window.Close();
        }
    }
}
