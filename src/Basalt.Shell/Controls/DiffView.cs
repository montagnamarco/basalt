using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

/// <summary>What a line in a diff represents.</summary>
public enum DiffLineKind { Context, Added, Removed, Header }

/// <summary>One line of a unified diff.</summary>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldLine, int? SoNewLine)
{
    public string Display => Text;
}

/// <summary>
/// A unified diff, coloured the way git colours it.
///
/// Reading git's own output rather than computing a difference here keeps the
/// view honest: what is shown is exactly what would be committed.
/// </summary>
public sealed class DiffView : UserControl
{
    private readonly ItemsControl _lines;
    private readonly EmptyState _empty;

    public DiffView()
    {
        _empty = new EmptyState(Localizer.Get(StringKeys.DiffNoChanges), IconKind.Diff);

        _lines = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<DiffLine>((line, _) => BuildLine(line))
        };

        Content = _empty;
    }

    internal IReadOnlyList<DiffLine> Lines { get; private set; } = [];

    private static Control BuildLine(DiffLine? line)
    {
        if (line is null) return new TextBlock();

        var text = new TextBlock
        {
            Text = line.Display,
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = PanelMetrics.FontSize,
        };

        // The header colour was a fixed slate grey, which is a pale smudge on a
        // dark surface; the description token is the theme's own secondary text.
        if (line.Kind == DiffLineKind.Header)
            text.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        var row = new Border { Padding = new Thickness(Spacing.Small, 0), Child = text };

        // Washes of the theme's success and error colours: green and red keep
        // their meaning in every theme and stay readable behind the text.
        switch (line.Kind)
        {
            case DiffLineKind.Added: row.ThemedTint(Border.BackgroundProperty, "SuccessForeground", 0.15); break;
            case DiffLineKind.Removed: row.ThemedTint(Border.BackgroundProperty, "ErrorForeground", 0.15); break;
        }

        return row;
    }

    public void Show(string unifiedDiff)
    {
        Lines = Parse(unifiedDiff);

        if (Lines.Count == 0)
        {
            Content = _empty;
            return;
        }

        _lines.ItemsSource = Lines;
        Content = _lines;
    }

    public void Clear() => Show("");

    /// <summary>
    /// Reads a unified diff into lines, tracking the line numbers.
    ///
    /// The numbers come from the hunk headers ("@@ -a,b +c,d @@") because a
    /// diff omits unchanged regions: counting from the start of the file would
    /// put every line after the first hunk in the wrong place.
    /// </summary>
    internal static IReadOnlyList<DiffLine> Parse(string unifiedDiff)
    {
        if (unifiedDiff.Length == 0) return [];

        var lines = new List<DiffLine>();
        var oldLine = 0;
        var newLine = 0;

        foreach (var raw in unifiedDiff.Split('\n'))
        {
            var text = raw.TrimEnd('\r');

            if (text.StartsWith("@@", StringComparison.Ordinal))
            {
                (oldLine, newLine) = ParseHunkHeader(text);
                lines.Add(new DiffLine(DiffLineKind.Header, text, null, null));
                continue;
            }

            // Everything before the first hunk is git's own preamble.
            if (text.StartsWith("diff ", StringComparison.Ordinal) ||
                text.StartsWith("index ", StringComparison.Ordinal) ||
                text.StartsWith("--- ", StringComparison.Ordinal) ||
                text.StartsWith("+++ ", StringComparison.Ordinal) ||
                text.StartsWith("new file", StringComparison.Ordinal) ||
                text.StartsWith("deleted file", StringComparison.Ordinal))
            {
                lines.Add(new DiffLine(DiffLineKind.Header, text, null, null));
                continue;
            }

            if (text.Length == 0) continue;

            switch (text[0])
            {
                case '+':
                    lines.Add(new DiffLine(DiffLineKind.Added, text, null, newLine));
                    newLine++;
                    break;

                case '-':
                    lines.Add(new DiffLine(DiffLineKind.Removed, text, oldLine, null));
                    oldLine++;
                    break;

                case '\\':
                    // "\ No newline at end of file" describes the line above.
                    lines.Add(new DiffLine(DiffLineKind.Header, text, null, null));
                    break;

                default:
                    lines.Add(new DiffLine(DiffLineKind.Context, text, oldLine, newLine));
                    oldLine++;
                    newLine++;
                    break;
            }
        }

        return lines;
    }

    private static (int Old, int New) ParseHunkHeader(string header)
    {
        // "@@ -12,7 +12,9 @@ optional context"
        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var old = 0;
        var updated = 0;

        foreach (var part in parts)
        {
            if (part.StartsWith('-')) old = FirstNumber(part[1..]);
            else if (part.StartsWith('+')) updated = FirstNumber(part[1..]);
        }

        return (old, updated);
    }

    private static int FirstNumber(string text)
    {
        var comma = text.IndexOf(',');
        var number = comma < 0 ? text : text[..comma];

        return int.TryParse(number, out var value) ? value : 0;
    }
}
