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

        // The first of each name wins rather than throwing. A form with two
        // controls called the same thing is not something we should refuse to
        // save — Visual Basic 6 allowed it through control arrays, and a
        // duplicate here took the whole save down with an exception naming a
        // key rather than a form.
        var positions = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in edited.Descendants())
        {
            var name = NameOf(element);

            if (name is { Length: > 0 } && !positions.ContainsKey(name))
                positions[name] = element;
        }

        // Kept as they were written, because a .frm is CRLF and a file that
        // comes back with different endings is a whole-file change in every
        // diff the author looks at afterwards.
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = original.Replace("\r\n", "\n").Split('\n');

        // Which names the file already knows. Anything in the markup that is
        // not here is a control the designer added, and it has to be written
        // into the form or it is lost the moment the file is saved — drawn,
        // visible, and gone, with nothing saying why.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith("Begin ", StringComparison.Ordinal)) continue;

            if (NameIn(trimmed) is { Length: > 0 } name) known.Add(name);
        }

        var added = positions
            .Where(p => !known.Contains(p.Key))
            .ToList();

        var written = new StringBuilder();
        var current = (string?)null;
        var depth = 0;
        var inserted = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Before the form's own End, which closes the outermost Begin: a
            // control written after it is outside the form and Visual Basic 6
            // refuses to open the file at all.
            if (!inserted && depth == 1 && trimmed == "End" && added.Count > 0)
            {
                foreach (var (name, element) in added)
                    WriteNewControl(written, name, element, newline);

                inserted = true;
            }

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
    /// Writes a control the designer added, as Visual Basic 6 would have.
    /// </summary>
    /// <remarks>
    /// Three-space indentation and the property names lined up in column
    /// twenty, which is what Visual Basic wrote: a form that comes back
    /// formatted differently from the rest of itself reads as damaged even
    /// when it opens.
    /// </remarks>
    private static void WriteNewControl(
        StringBuilder written, string name, XElement element, string newline)
    {
        var type = Vb6TypeFor(element.Name.LocalName);

        written.Append($"   Begin {type} {name} ").Append(newline);

        foreach (var (property, attribute) in new[]
                 {
                     ("Height", "Height"),
                     ("Left", "Canvas.Left"),
                     ("Top", "Canvas.Top"),
                     ("Width", "Width"),
                 })
        {
            if (Twips(element, attribute) is { } value)
                written.Append("      ").Append(property.PadRight(16))
                       .Append("=   ").Append(value).Append(newline);
        }

        if (element.Attribute("Content")?.Value is { } content)
            written.Append("      ").Append("Caption".PadRight(16))
                   .Append("=   \"").Append(content.Replace("\"", "\"\""))
                   .Append('"').Append(newline);

        written.Append("   End").Append(newline);
    }

    /// <summary>
    /// The Visual Basic 6 control an Avalonia one stands for.
    /// </summary>
    /// <remarks>
    /// The reverse of the mapping the reader uses, and not quite its inverse:
    /// several VB6 controls become the same Avalonia one — a Line and a Shape
    /// are both a Rectangle — so a new Rectangle has to be called something,
    /// and Shape is the one that can be either.
    /// </remarks>
    private static string Vb6TypeFor(string avalonia) => avalonia switch
    {
        "TextBox" => "VB.TextBox",
        "Button" => "VB.CommandButton",
        "TextBlock" => "VB.Label",
        "CheckBox" => "VB.CheckBox",
        "RadioButton" => "VB.OptionButton",
        "ComboBox" => "VB.ComboBox",
        "ListBox" => "VB.ListBox",
        "HeaderedContentControl" => "VB.Frame",
        "Border" => "VB.PictureBox",
        "Image" => "VB.Image",
        "ScrollBar" => "VB.HScrollBar",
        "Rectangle" => "VB.Shape",
        _ => "VB.Label",
    };

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
