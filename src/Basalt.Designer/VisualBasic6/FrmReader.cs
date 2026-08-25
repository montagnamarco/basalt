namespace Basalt.Designer.VisualBasic6;

/// <summary>
/// Reads a VB6 .frm file.
///
/// The format is plain text and nests by indentation of keywords rather than
/// by brackets:
///
///     VERSION 5.00
///     Begin VB.Form Form1
///        Caption = "Hello"
///        ClientWidth = 4800
///        Begin VB.CommandButton Command1
///           Caption = "Press me"
///        End
///     End
///     Attribute VB_Name = "Form1"
///     Private Sub Command1_Click()
///     ...
///
/// Everything after the last End of the object description is Basic code.
///
/// Written as a reader first, before any designer work: the plan asks to
/// understand VB6 before designing for it, and a format is understood by
/// reading real files rather than by describing it from memory.
/// </summary>
public static class FrmReader
{
    /// <summary>
    /// Reads a form, or says what stopped it.
    ///
    /// A file that cannot be read gives back null and a reason rather than a
    /// half-built form: a designer opening a broken file should say so.
    /// </summary>
    public static (FrmForm? Form, string? Problem) Read(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var index = 0;

        // VERSION and anything before the first Begin is not part of the form.
        while (index < lines.Length
               && !lines[index].TrimStart().StartsWith("Begin ", StringComparison.OrdinalIgnoreCase))
        {
            index++;
        }

        if (index >= lines.Length)
            return (null, "This file has no form in it: there is no Begin.");

        var root = ReadControl(lines, ref index, out var problem);

        if (root is null) return (null, problem);

        // What follows is the code behind, less the Attribute lines VB6 puts
        // between the two.
        var code = string.Join("\n", lines.Skip(index)
            .SkipWhile(line => line.TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase)
                            || line.Trim().Length == 0));

        return (new FrmForm(root) { Code = code.TrimEnd() }, null);
    }

    /// <summary>
    /// Reads one Begin...End block and whatever is inside it.
    ///
    /// <paramref name="index"/> is left just past the End, so the caller
    /// carries on from there.
    /// </summary>
    private static FrmControl? ReadControl(string[] lines, ref int index, out string? problem)
    {
        problem = null;

        var header = lines[index].Trim();

        // "Begin VB.CommandButton Command1"
        var words = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 3)
        {
            problem = $"Line {index + 1} begins a control without saying which: '{header}'.";
            return null;
        }

        var control = new FrmControl(words[1], words[2]);

        index++;

        while (index < lines.Length)
        {
            var line = lines[index].Trim();

            if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                return control;
            }

            if (line.StartsWith("Begin ", StringComparison.OrdinalIgnoreCase))
            {
                var child = ReadControl(lines, ref index, out problem);

                if (child is null) return null;

                control.Children.Add(child);
                continue;
            }

            // "BeginProperty Font ... EndProperty" wraps a group of settings.
            // Skipped rather than half-read: a font needs converting on its
            // own terms, and reading it as loose properties would lose which
            // control it belonged to.
            if (line.StartsWith("BeginProperty", StringComparison.OrdinalIgnoreCase))
            {
                SkipProperty(lines, ref index);
                continue;
            }

            if (line.Length > 0 && line.IndexOf('=') is var equals and > 0)
            {
                control.Properties[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }

            index++;
        }

        problem = $"The control '{control.Name}' is never closed with End.";
        return null;
    }

    /// <summary>Steps over a BeginProperty block, however deeply nested.</summary>
    private static void SkipProperty(string[] lines, ref int index)
    {
        var depth = 0;

        while (index < lines.Length)
        {
            var line = lines[index].Trim();

            if (line.StartsWith("BeginProperty", StringComparison.OrdinalIgnoreCase)) depth++;
            if (line.StartsWith("EndProperty", StringComparison.OrdinalIgnoreCase)) depth--;

            index++;

            if (depth == 0) return;
        }
    }

    /// <summary>
    /// A VB6 twip as a device-independent pixel.
    ///
    /// VB6 measures in twips: 1440 to the inch, and 15 to a pixel at the 96
    /// dpi it assumed. Avalonia's unit is 1/96 inch, so the two agree at that
    /// scale and a twip is exactly a fifteenth of one.
    /// </summary>
    public static double TwipsToPixels(double twips) => twips / 15.0;

    /// <summary>The other way, for writing a .frm back out.</summary>
    public static double PixelsToTwips(double pixels) => pixels * 15.0;
}
