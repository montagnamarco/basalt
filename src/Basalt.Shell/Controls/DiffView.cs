using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;

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
    private static readonly IBrush AddedBackground =
        new SolidColorBrush(Color.FromRgb(0x22, 0x86, 0x36), 0.15);

    private static readonly IBrush RemovedBackground =
        new SolidColorBrush(Color.FromRgb(0xCF, 0x22, 0x2E), 0.15);

    private static readonly IBrush HeaderForeground =
        new SolidColorBrush(Color.FromRgb(0x57, 0x5F, 0x6D));

    private readonly ItemsControl _lines;
    private readonly TextBlock _empty;

    public DiffView()
    {
        _empty = new TextBlock
        {
            Text = "No changes.",
            Opacity = 0.6,
            FontSize = 12,
            Margin = new Thickness(12, 8, 8, 8)
        };

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

        return new Border
        {
            Background = line.Kind switch
            {
                DiffLineKind.Added => AddedBackground,
                DiffLineKind.Removed => RemovedBackground,
                _ => null
            },
            Padding = new Thickness(6, 0, 6, 0),
            Child = new TextBlock
            {
                Text = line.Display,
                FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
                FontSize = 12,
                Foreground = line.Kind == DiffLineKind.Header ? HeaderForeground : null
            }
        };
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
