using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Clicking the margin to set a breakpoint.
///
/// These raise the pointer event rather than calling Toggle: ten tests called
/// Toggle directly and passed while the click did nothing, which is exactly
/// the gap that let a broken feature look finished.
/// </summary>
public sealed class BreakpointMarginClickTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-bpclick", Guid.NewGuid().ToString("N"));

    public BreakpointMarginClickTests() => Directory.CreateDirectory(_root);

    private async Task<(TestWindow Host, CodeEditor Code, TextEditor Editor)> OpenAsync(
        string content = "Module A\n    Sub M()\n        Dim x = 1\n    End Sub\nEnd Module")
    {
        var file = Path.Combine(_root, "Program.vb");
        await File.WriteAllTextAsync(file, content);

        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var code = host.Window.GetVisualDescendants().OfType<CodeEditor>().Single();
        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();

        return (host, code, editor);
    }

    /// <summary>
    /// Clicks the margin at the vertical middle of a line.
    ///
    /// The position is given relative to the text view and then translated to
    /// the margin: the margin sits lower in the window than the view does, and
    /// a position taken from the view alone lands tens of pixels out — above
    /// the first line, where there is nothing to toggle.
    /// </summary>
    private static void ClickLine(
        BreakpointMargin margin, TextEditor editor, int line, Visual window)
    {
        var view = editor.TextArea.TextView;

        var visualLine = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(line));

        var inView = new Point(
            0, visualLine.VisualTop - view.VerticalOffset + (visualLine.Height / 2));

        // A raised event carries a position in the window's coordinates, while
        // the handler reads it back relative to the margin. The difference is
        // where the margin sits in the window, so it has to be added here for
        // the handler to see the line that was aimed at.
        var origin = margin.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("The margin is not in the window.");

        margin.RaiseEvent(new PointerPressedEventArgs(
            margin,
            new Pointer(0, PointerType.Mouse, isPrimary: true),
            margin,
            new Point(margin.Bounds.Width / 2, inView.Y + origin.Y),
            timestamp: 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
    }

    [AvaloniaFact]
    public async Task ClickingTheMarginSetsABreakpoint()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        ClickLine(code.BreakpointMargin, editor, 3, host.Window);

        Assert.Contains(3, code.BreakpointMargin.Lines);
    }

    [AvaloniaFact]
    public async Task ClickingTheSameLineAgainClearsIt()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        ClickLine(code.BreakpointMargin, editor, 3, host.Window);
        ClickLine(code.BreakpointMargin, editor, 3, host.Window);

        Assert.Empty(code.BreakpointMargin.Lines);
    }

    [AvaloniaFact]
    public async Task ClickingReportsTheLineThatWasClicked()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        var reported = new List<int>();
        code.BreakpointMargin.Toggled += (_, line) => reported.Add(line);

        ClickLine(code.BreakpointMargin, editor, 2, host.Window);

        Assert.Equal([2], reported);
    }

    [AvaloniaFact]
    public async Task ClicksOnDifferentLinesSetDifferentBreakpoints()
    {
        var (host, code, editor) = await OpenAsync();
        using var _ = host;

        ClickLine(code.BreakpointMargin, editor, 2, host.Window);
        ClickLine(code.BreakpointMargin, editor, 4, host.Window);

        Assert.Contains(2, code.BreakpointMargin.Lines);
        Assert.Contains(4, code.BreakpointMargin.Lines);
    }

    [AvaloniaFact]
    public async Task TheMarginIsWideEnoughToClickComfortably()
    {
        // A strip a few pixels wide is a target the user misses.
        var (host, code, _) = await OpenAsync();
        using var __ = host;

        Assert.True(code.BreakpointMargin.Bounds.Width >= 18,
            $"The margin is only {code.BreakpointMargin.Bounds.Width}px wide.");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
