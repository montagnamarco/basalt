using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Extensibility;

namespace Basalt.Shell.Controls;

/// <summary>
/// Draws what the IDE knows about a symbol.
///
/// One control for every language: it is handed a description and never asks
/// where it came from. Visual Basic fills that description from Roslyn and
/// QuickBASIC from a table of intrinsics, and both look the same here — which
/// is the whole point of describing symbols in a shape no language owns.
/// </summary>
public sealed class SymbolDescriptionView : UserControl
{
    private static readonly FontFamily Mono =
        new("Menlo,Consolas,DejaVu Sans Mono,monospace");

    public SymbolDescriptionView(SymbolDescription description, string? counter = null)
    {
        var lines = new StackPanel { Spacing = 4, MaxWidth = 520 };

        // Room to grow downwards: a call of five documented parameters is
        // taller than a tooltip's idea of a line, and clipping it hides the
        // one the reader is on.
        lines.VerticalAlignment = VerticalAlignment.Top;

        lines.Children.Add(SignatureLine(description, counter));

        if (description.Documentation is { Length: > 0 } documentation)
        {
            lines.Children.Add(new TextBlock
            {
                Text = documentation,
                FontSize = 12,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap
            });
        }

        // The parameters one per line, so a call of five arguments can be read
        // rather than parsed.
        if (description.Parameters.Count > 0)
            lines.Children.Add(ParameterList(description));

        Content = lines;
    }

    /// <summary>
    /// The signature, with the parameter being written in bold.
    ///
    /// The bold is the point: in a call of five arguments it is what says
    /// where you are.
    /// </summary>
    private Control SignatureLine(SymbolDescription description, string? counter)
    {
        var text = new TextBlock
        {
            FontFamily = Mono,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        var active = ActiveParameterName(description);

        foreach (var part in description.Signature)
        {
            var isActive = active is { Length: > 0 }
                && part.Kind == SymbolPartKind.ParameterName
                && string.Equals(part.Text, active, StringComparison.Ordinal);

            var run = new Run(part.Text)
            {
                FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal
            };

            // Only when there is a colour to give: assigning null asks for
            // "no brush" rather than "inherit", and the run vanishes.
            if (BrushFor(part.Kind, isActive) is { } brush) run.Foreground = brush;

            text.Inlines!.Add(run);
        }

        if (counter is not { Length: > 0 }) return text;

        // "1 of 3" above rather than beside: on the same line it takes width
        // from the signature, which then wraps in the middle of a parameter
        // list that would otherwise have fitted.
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = counter,
                    FontSize = 11,
                    Opacity = 0.6
                },
                text
            }
        };
    }

    /// <summary>The parameters, with the one being written marked.</summary>
    private static Control ParameterList(SymbolDescription description)
    {
        var list = new StackPanel { Spacing = 1, Margin = new Thickness(0, 4, 0, 0) };

        for (var i = 0; i < description.Parameters.Count; i++)
        {
            var parameter = description.Parameters[i];
            var isActive = i == description.ActiveParameter;

            var line = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = isActive ? 1 : 0.7
            };

            line.Inlines!.Add(new Run(parameter.Display)
            {
                FontFamily = Mono,
                FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal
            });

            if (parameter.Documentation is { Length: > 0 } documentation)
                line.Inlines!.Add(new Run($"  —  {documentation}"));

            list.Children.Add(line);
        }

        return list;
    }

    /// <summary>Which parameter name the active index points at.</summary>
    private static string? ActiveParameterName(SymbolDescription description)
    {
        var at = description.ActiveParameter;

        return at >= 0 && at < description.Parameters.Count
            ? description.Parameters[at].Name
            : null;
    }

    /// <summary>
    /// The colour a run is drawn in.
    ///
    /// Deliberately quiet: a tooltip is read at a glance, and colouring every
    /// kind differently turns it into a paint chart.
    /// </summary>
    private static IBrush? BrushFor(SymbolPartKind kind, bool isActive) => kind switch
    {
        _ when isActive => null,
        SymbolPartKind.Keyword => new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0xC0)),
        SymbolPartKind.Type => new SolidColorBrush(Color.FromRgb(0x2B, 0x91, 0xAF)),
        _ => null
    };
}
