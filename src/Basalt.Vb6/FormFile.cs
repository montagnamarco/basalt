using System.Text;

namespace Basalt.Vb6;

/// <summary>
/// A Visual Basic 6 form, read from its .frm file.
/// </summary>
/// <remarks>
/// A .frm is two documents in one file: a block of nested Begin/End
/// declarations describing the controls, then the form's own Visual Basic
/// after the VB_ attributes. Both halves are plain text, which is what makes
/// reading one possible at all without Visual Basic installed — a control
/// from an OCX is named and described here even when the OCX itself is
/// nowhere to be found.
/// </remarks>
public sealed class FormFile
{
    private FormFile(
        FormControl root, string code, IReadOnlyList<string> objects, int codeLine)
    {
        Root = root;
        Code = code;
        Objects = objects;
        CodeLine = codeLine;
    }

    /// <summary>The form itself, with its controls beneath it.</summary>
    public FormControl Root { get; }

    /// <summary>The Visual Basic written in the form.</summary>
    public string Code { get; }

    /// <summary>The OCX files the form declares it needs.</summary>
    public IReadOnlyList<string> Objects { get; }

    /// <summary>
    /// Which line of the .frm the code begins on, counting from one.
    /// </summary>
    /// <remarks>
    /// For #ExternalSource, so an error is reported against the .frm at the
    /// line the author wrote rather than against generated code they never
    /// saw — and so a breakpoint set in the .frm is hit.
    /// </remarks>
    public int CodeLine { get; }

    /// <summary>Reads a form from the text of a .frm file.</summary>
    public static FormFile Parse(string text)
    {
        // Written by Visual Basic with CRLF, and read here from wherever it
        // has been since: a file that has been through a version control
        // system or a zip may have either ending.
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var objects = new List<string>();
        var stack = new Stack<FormControl>();
        FormControl? root = null;
        var codeFrom = lines.Length;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // The code begins after the last VB_ attribute. Everything before
            // it is the designer's, and everything after is the author's.
            if (line.StartsWith("Attribute VB_", StringComparison.Ordinal))
            {
                codeFrom = i + 1;
                continue;
            }

            if (line.StartsWith("Object =", StringComparison.Ordinal)
                || line.StartsWith("Object=", StringComparison.Ordinal))
            {
                var file = OcxFileIn(line);

                if (file is not null) objects.Add(file);

                continue;
            }

            if (line.StartsWith("Begin ", StringComparison.Ordinal))
            {
                var control = BeginningAt(line);

                if (stack.Count > 0)
                    stack.Peek().Children.Add(control);
                else
                    root ??= control;

                stack.Push(control);
                continue;
            }

            // BeginProperty describes a sub-object — a font, a column header.
            // Skipped rather than read: its shape differs per control, and a
            // wrong guess would write a property the target does not have.
            if (line.StartsWith("BeginProperty", StringComparison.Ordinal))
            {
                var depth = 1;

                // Stopping on the EndProperty rather than past it: the loop
                // advanced i and then the for advanced it again, so the line
                // after EndProperty was skipped. That line is the control's
                // own End, and losing it left the stack one deep for the rest
                // of the file — everything after an OCX went inside it, and a
                // control added at the end was never seen as the form's.
                while (i + 1 < lines.Length && depth > 0)
                {
                    var inner = lines[++i].Trim();

                    if (inner.StartsWith("BeginProperty", StringComparison.Ordinal)) depth++;
                    else if (inner.StartsWith("EndProperty", StringComparison.Ordinal)) depth--;
                }

                continue;
            }

            if (line == "End")
            {
                if (stack.Count > 0) stack.Pop();
                continue;
            }

            if (stack.Count > 0 && line.Contains('='))
            {
                var (name, value) = PropertyIn(line);

                if (name is not null) stack.Peek().Properties[name] = value;
            }
        }

        var code = string.Join("\n", lines.Skip(codeFrom)).Trim();

        return new FormFile(
            root ?? new FormControl("VB.Form", "Form1"), code, objects, codeFrom + 1);
    }

    /// <summary>The control a Begin line declares.</summary>
    private static FormControl BeginningAt(string line)
    {
        // "Begin VB.TextBox txtNome" — the type carries its library, which is
        // what tells an intrinsic control from one that came out of an OCX.
        var parts = line.Substring("Begin ".Length)
            .Split([' '], StringSplitOptions.RemoveEmptyEntries);

        return new FormControl(
            parts.Length > 0 ? parts[0] : "VB.Form",
            parts.Length > 1 ? parts[1] : "");
    }

    /// <summary>A property line, as name and value.</summary>
    private static (string? Name, string Value) PropertyIn(string line)
    {
        var at = line.IndexOf('=');

        if (at <= 0) return (null, "");

        var name = line.Substring(0, at).Trim();
        var value = line.Substring(at + 1).Trim();

        // Visual Basic writes a comment after some values —
        // "StartUpPosition = 3  'Windows Default" — and keeping it would put
        // the comment into the property.
        var comment = IndexOfCommentIn(value);

        if (comment >= 0) value = value.Substring(0, comment).Trim();

        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            value = value.Substring(1, value.Length - 2).Replace("\"\"", "\"");

        return (name, value);
    }

    /// <summary>Where a trailing comment starts, or -1.</summary>
    private static int IndexOfCommentIn(string value)
    {
        var quoted = false;

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '"') quoted = !quoted;
            if (value[i] == '\'' && !quoted) return i;
        }

        return -1;
    }

    /// <summary>The OCX an Object line names.</summary>
    private static string? OcxFileIn(string line)
    {
        // Object = "{GUID}#2.0#0"; "MSCOMCTL.OCX"
        var semicolon = line.LastIndexOf(';');

        if (semicolon < 0) return null;

        return line.Substring(semicolon + 1).Trim().Trim('"');
    }
}

/// <summary>One control in a form, with the controls inside it.</summary>
public sealed class FormControl(string type, string name)
{
    /// <summary>The type as the file writes it: VB.TextBox, MSComctlLib.ListView.</summary>
    public string Type { get; } = type;

    public string Name { get; } = name;

    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<FormControl> Children { get; } = [];

    /// <summary>
    /// Whether this control is one Visual Basic itself provided.
    /// </summary>
    /// <remarks>
    /// The intrinsic controls are the ones with no OCX behind them, and they
    /// are what most forms are made of. Anything else came from a library that
    /// may not exist on this machine and certainly does not exist on this
    /// platform.
    /// </remarks>
    public bool IsIntrinsic =>
        Type.StartsWith("VB.", StringComparison.OrdinalIgnoreCase);

    /// <summary>A property as a number, or a default.</summary>
    public double Number(string name, double fallback = 0) =>
        Properties.TryGetValue(name, out var value)
        && double.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public string? Text(string name) =>
        Properties.TryGetValue(name, out var value) ? value : null;

    public override string ToString() => $"{Type} {Name}";
}
