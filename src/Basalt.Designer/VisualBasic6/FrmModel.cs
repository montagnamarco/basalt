namespace Basalt.Designer.VisualBasic6;

/// <summary>
/// A control on a VB6 form, as the .frm file describes it.
///
/// Properties are kept as the text they were written as rather than being
/// converted while reading: what a value means depends on the control, and a
/// reader that guesses loses the original. Converting is the next step's job.
/// </summary>
/// <param name="TypeName">The VB6 control, such as "VB.CommandButton".</param>
/// <param name="Name">The control's name, which is also its variable name.</param>
public sealed record FrmControl(string TypeName, string Name)
{
    public Dictionary<string, string> Properties { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Controls inside this one, for frames and picture boxes.</summary>
    public List<FrmControl> Children { get; } = [];

    /// <summary>A property as a number, or null when it is not one.</summary>
    public double? Number(string name) =>
        Properties.TryGetValue(name, out var text)
        && double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// A property as text, with the quotes taken off.
    ///
    /// VB6 writes a string property quoted, and a caller wants what was
    /// inside the quotes.
    /// </summary>
    public string? Text(string name) =>
        Properties.TryGetValue(name, out var value)
            ? value.Trim('"')
            : null;

    /// <summary>Every control under this one, including itself.</summary>
    public IEnumerable<FrmControl> Descend()
    {
        yield return this;

        foreach (var child in Children)
            foreach (var found in child.Descend())
                yield return found;
    }
}

/// <summary>
/// A VB6 form, read from a .frm file.
///
/// The file holds both the form and the code behind it, separated by where
/// the object description ends, so both come out of one read.
/// </summary>
public sealed record FrmForm(FrmControl Root)
{
    /// <summary>The form's name.</summary>
    public string Name => Root.Name;

    /// <summary>
    /// The Basic code that followed the form description.
    ///
    /// Kept as it was written: it is a QuickBASIC-like dialect this IDE
    /// already reads, and rewriting it is a later decision.
    /// </summary>
    public string Code { get; init; } = "";

    /// <summary>What VB6 said it was, such as "VB.Form".</summary>
    public string TypeName => Root.TypeName;

    /// <summary>Every control on the form, the form itself aside.</summary>
    public IReadOnlyList<FrmControl> Controls => [.. Root.Descend().Skip(1)];

    /// <summary>
    /// The event handlers the code defines, by the control and event they
    /// belong to.
    ///
    /// VB6 ties a handler to a control by its name: Command1_Click belongs to
    /// Command1. That convention is what makes a form and its code one thing.
    /// </summary>
    public IReadOnlyList<(string Control, string Event)> Handlers =>
    [
        .. Code.Split('\n')
            .Select(line => line.Trim())
            .Where(line =>
                line.StartsWith("Private Sub ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Public Sub ", StringComparison.OrdinalIgnoreCase))
            .Select(NameOfHandler)
            .Where(pair => pair is not null)
            .Select(pair => pair!.Value)
    ];

    /// <summary>The control and event a "Private Sub Command1_Click()" names.</summary>
    private static (string Control, string Event)? NameOfHandler(string line)
    {
        var open = line.IndexOf('(');
        if (open < 0) return null;

        var words = line[..open].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;

        var name = words[^1];
        var underscore = name.LastIndexOf('_');

        return underscore <= 0 || underscore == name.Length - 1
            ? null
            : (name[..underscore], name[(underscore + 1)..]);
    }
}
