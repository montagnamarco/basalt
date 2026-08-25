using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basalt.Shell;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The window that says what Basalt is.
///
/// It used to be three lines of text: no icon, no runtime, and nothing that
/// could be copied. A bug report starts here.
/// </summary>
public class AboutWindowTests
{
    private static (AboutWindow Window, IReadOnlyList<string> Text) Open()
    {
        var window = new AboutWindow();

        window.Show();
        window.UpdateLayout();

        var text = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text ?? "")
            .Concat(window.GetVisualDescendants()
                .OfType<SelectableTextBlock>()
                .Select(t => t.Text ?? ""))
            .Where(t => t.Length > 0)
            .ToList();

        return (window, text);
    }

    [AvaloniaFact]
    public void ItWearsTheApplicationIcon()
    {
        var (window, _) = Open();

        var icons = window.GetVisualDescendants().OfType<IconView>().ToList();

        Assert.Contains(icons, i => i.Kind == IconKind.Application);

        window.Close();
    }

    [AvaloniaFact]
    public void ItSaysWhatItIsRunningOn()
    {
        // What a bug report needs: the version, the runtime, the system.
        var (window, text) = Open();

        var all = string.Join(" ", text);

        Assert.Contains("Basalt", all, StringComparison.Ordinal);
        Assert.Contains("Version", all, StringComparison.Ordinal);
        Assert.Contains(".NET", all, StringComparison.Ordinal);
        Assert.Contains("licence", all, StringComparison.OrdinalIgnoreCase);

        window.Close();
    }

    [AvaloniaFact]
    public void TheDetailsCanBeSelected()
    {
        // Read off a screen and typed back in by hand otherwise.
        var (window, _) = Open();

        var selectable = window.GetVisualDescendants()
            .OfType<SelectableTextBlock>()
            .ToList();

        Assert.NotEmpty(selectable);

        window.Close();
    }

    [AvaloniaFact]
    public void TheDetailsAreWorthCopying()
    {
        var window = new AboutWindow();

        Assert.Contains("Basalt", window.Details, StringComparison.Ordinal);
        Assert.Contains(".NET", window.Details, StringComparison.Ordinal);

        // Several lines, so pasting it into a report reads as a report.
        Assert.True(window.Details.Split('\n').Length >= 3);
    }

    [AvaloniaFact]
    public void EscapeClosesIt()
    {
        var (window, _) = Open();

        var closed = false;

        window.Closed += (_, _) => closed = true;

        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape
        });

        Assert.True(closed);
    }

    [AvaloniaFact]
    public void TheSentenceFitsInTheWindow()
    {
        // Inside a horizontal StackPanel a child is offered infinite width,
        // so TextWrapping does nothing and the sentence runs off the edge.
        var (window, _) = Open();

        var wrapped = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => (t.Text ?? "").Contains("dialects", StringComparison.Ordinal))
            .ToList();

        Assert.All(wrapped, t => Assert.True(t.Bounds.Width <= window.Width,
            $"the text is {t.Bounds.Width} wide in a window of {window.Width}"));

        window.Close();
    }
}
