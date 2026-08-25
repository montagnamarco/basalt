using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Basalt.Extensibility;
using Basalt.QuickBasic;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The one control that draws a symbol, for every language.
///
/// It is handed a description and never asks where it came from: these tests
/// build descriptions by hand and from QuickBASIC, and the control cannot
/// tell the difference.
/// </summary>
public class SymbolDescriptionViewTests
{
    private static IReadOnlyList<Run> RunsIn(Control view) =>
        [.. view.GetVisualDescendants()
            .OfType<TextBlock>()
            .SelectMany(t => t.Inlines?.OfType<Run>() ?? [])];

    private static Control Shown(SymbolDescriptionView view)
    {
        var window = new Window { Content = view, Width = 620, Height = 260 };

        window.Show();

        for (var i = 0; i < 3; i++) window.UpdateLayout();

        return view;
    }

    [AvaloniaFact]
    public void TheWholeSignatureIsDrawn()
    {
        // Every run must reach the screen: assigning a null brush asked for
        // "no colour" rather than "inherit", and most of the signature
        // disappeared.
        var description = new SymbolDescription(
        [
            SymbolPart.Name("MID$"),
            SymbolPart.Plain(" ("),
            SymbolPart.Parameter("text"),
            SymbolPart.Plain(" AS "),
            SymbolPart.Type("STRING"),
            SymbolPart.Plain(")")
        ]);

        var view = Shown(new SymbolDescriptionView(description));

        var drawn = string.Concat(RunsIn(view).Select(r => r.Text));

        Assert.Contains("MID$", drawn, StringComparison.Ordinal);
        Assert.Contains("text", drawn, StringComparison.Ordinal);
        Assert.Contains("STRING", drawn, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheParameterBeingWrittenIsBold()
    {
        // The point of the whole thing: in a call of five arguments it is
        // what says where you are.
        var description = new SymbolDescription(
        [
            SymbolPart.Name("Greet"),
            SymbolPart.Plain("("),
            SymbolPart.Parameter("name"),
            SymbolPart.Plain(", "),
            SymbolPart.Parameter("times"),
            SymbolPart.Plain(")")
        ])
        {
            Parameters = [new("name", "String"), new("times", "Integer")],
            ActiveParameter = 1
        };

        var view = Shown(new SymbolDescriptionView(description));

        var bold = RunsIn(view)
            .Where(r => r.FontWeight == FontWeight.Bold)
            .Select(r => r.Text ?? "")
            .ToList();

        Assert.Contains(bold, t => t.Contains("times", StringComparison.Ordinal));
        Assert.DoesNotContain(bold, t => t == "name");
    }

    [AvaloniaFact]
    public void NothingIsBoldWhenNoCallIsBeingWritten()
    {
        // Hovering a name is not writing a call.
        var description = new SymbolDescription([SymbolPart.Name("Greeter")]);

        var view = Shown(new SymbolDescriptionView(description));

        Assert.DoesNotContain(RunsIn(view), r => r.FontWeight == FontWeight.Bold);
    }

    [AvaloniaFact]
    public void TheDocumentationIsShown()
    {
        var description = new SymbolDescription([SymbolPart.Name("LEN")])
        {
            Documentation = "How many characters a string holds."
        };

        var view = Shown(new SymbolDescriptionView(description));

        var text = string.Join(" ", view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text ?? ""));

        Assert.Contains("characters", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void EachParameterGetsItsOwnLine()
    {
        var description = new SymbolDescription([SymbolPart.Name("MID$")])
        {
            Parameters =
            [
                new("text", "STRING") { Documentation = "The string." },
                new("start", "INTEGER") { Documentation = "Where to start." }
            ]
        };

        var view = Shown(new SymbolDescriptionView(description));

        var lines = view.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => string.Concat(t.Inlines?.OfType<Run>().Select(r => r.Text) ?? []))
            .Where(t => t.Contains("As", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Contains("The string.", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void TheOverloadCounterIsShownWhenThereIsOne()
    {
        var view = Shown(new SymbolDescriptionView(
            new SymbolDescription([SymbolPart.Name("Greet")]), "2 of 3"));

        var text = string.Join(" ", view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text ?? ""));

        Assert.Contains("2 of 3", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ItDrawsWhatQuickBasicGivesIt()
    {
        // The control is not told which language this came from.
        var provider = new QuickBasicSymbolDescriptionProvider();

        var text = "PRINT MID$(a$, 2, ";

        var set = await provider.DescribeCallAsync(
            new LanguageDocument("/p.bas", text), text.Length);

        Assert.NotNull(set);

        var view = Shown(new SymbolDescriptionView(set.Overloads[0], "1 of 1"));

        var bold = RunsIn(view)
            .Where(r => r.FontWeight == FontWeight.Bold)
            .Select(r => r.Text ?? "")
            .ToList();

        // The third argument is the one being written.
        Assert.Contains(bold, t => t.Contains("count", StringComparison.Ordinal));
    }
}
