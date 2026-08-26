using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Basalt.Vb6;

/// <summary>
/// Puts a designer's changes back into the .frm they came from.
/// </summary>
/// <remarks>
/// By editing the file rather than rewriting it. A .frm holds more than this
/// converter understands — fonts, colour depths, an OCX's own property bag,
/// the .frx offsets pointing at binary beside it — and regenerating one from
/// what we read would throw all of that away the first time somebody nudged a
/// button.
///
/// So the original text is the document: a moved control changes the four
/// lines that say where it is, and every other line is copied through byte for
/// byte. What we do not understand survives because it is never touched.
/// </remarks>
public static class AxamlToForm
{
    /// <summary>
    /// Writes the designer's positions back into a form's text.
    /// </summary>
    /// <param name="original">The .frm exactly as it is on disk.</param>
    /// <param name="markup">The markup the designer has been editing.</param>
    public static string Apply(string original, string markup)
    {
        var edited = XElement.Parse(markup);

        var positions = edited.Descendants()
            .Select(e => (Name: NameOf(e), Element: e))
            .Where(x => x.Name is { Length: > 0 })
            .ToDictionary(x => x.Name!, x => x.Element, StringComparer.OrdinalIgnoreCase);

        // Kept as they were written, because a .frm is CRLF and a file that
        // comes back with different endings is a whole-file change in every
        // diff the author looks at afterwards.
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = original.Replace("\r\n", "\n").Split('\n');

        var written = new StringBuilder();
        var current = (string?)null;
        var depth = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Only the designer half. After the VB_ attributes it is the
            // author's Visual Basic, where a property name means something
            // else entirely.
            if (trimmed.StartsWith("Attribute VB_", StringComparison.Ordinal)) current = null;

            if (trimmed.StartsWith("Begin ", StringComparison.Ordinal))
            {
                depth++;
                current = NameIn(trimmed);
            }
            else if (trimmed == "End" && depth > 0)
            {
                depth--;
                current = null;
            }
            else if (current is not null
                  && positions.TryGetValue(current, out var element)
                  && Rewritten(line, trimmed, element) is { } replacement)
            {
                written.Append(replacement).Append(newline);
                continue;
            }

            written.Append(line).Append(newline);
        }

        // A newline is appended after every line, and a file ending in one
        // gives Split an empty last piece — so the text comes back with one
        // newline too many. Trimmed once here, which leaves the file ending
        // exactly as it began: a form that gained a blank line is a change to
        // a file nobody edited, and it shows in every diff afterwards.
        var text = written.ToString();

        return text.EndsWith(newline, StringComparison.Ordinal)
            ? text.Substring(0, text.Length - newline.Length)
            : text;
    }

    /// <summary>
    /// The line as it should now read, or null to leave it alone.
    /// </summary>
    private static string? Rewritten(string line, string trimmed, XElement element)
    {
        var at = trimmed.IndexOf('=');

        if (at <= 0) return null;

        var property = trimmed.Substring(0, at).Trim();

        var value = property switch
        {
            "Left" => Twips(element, "Canvas.Left"),
            "Top" => Twips(element, "Canvas.Top"),
            "Width" => Twips(element, "Width"),
            "Height" => Twips(element, "Height"),
            _ => null,
        };

        if (value is null) return null;

        // Only when the control really moved. Twips do not survive the round
        // trip through pixels — 4400 becomes 293 becomes 4395 — so a form
        // opened and saved without a single drag came back with almost every
        // size altered by a few twips, and the diff was the whole file.
        //
        // The tolerance is the rounding itself: anything within half a pixel
        // is the same position said two ways.
        var written = trimmed.Substring(at + 1).Trim();

        if (double.TryParse(written, NumberStyles.Any, CultureInfo.InvariantCulture, out var was)
         && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var now)
         && Math.Abs(was - now) <= FormToAxaml.TwipsPerPixel / 2)
            return null;

        // Everything up to and including the "=" is kept exactly as Visual
        // Basic wrote it, spacing included, so only the number changes. Rebuilt
        // instead, the file comes back reformatted and every property line
        // shows up in the diff.
        var upToEquals = line.Substring(0, line.IndexOf('=') + 1);
        var afterEquals = line.Substring(line.IndexOf('=') + 1);
        var spaces = afterEquals.Length - afterEquals.TrimStart().Length;

        return upToEquals + new string(' ', Math.Max(1, spaces)) + value;
    }

    /// <summary>A markup value in twips, or null when it is not there.</summary>
    private static string? Twips(XElement element, string attribute)
    {
        var value = element.Attribute(attribute)?.Value;

        if (value is null) return null;

        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pixels)
            ? Math.Round(pixels * FormToAxaml.TwipsPerPixel).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>The name a Begin line gives its control.</summary>
    private static string? NameIn(string line)
    {
        var parts = line.Substring("Begin ".Length)
            .Split([' '], StringSplitOptions.RemoveEmptyEntries);

        return parts.Length > 1 ? parts[1] : null;
    }

    /// <summary>The x:Name an element carries.</summary>
    private static string? NameOf(XElement element) =>
        element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == "Name")
            ?.Value;
}
