namespace Basalt.Workspace;

/// <summary>
/// What Visual Studio gives a control when it is dropped on a form: its size, its text, whether it sizes itself and whether it
/// docks. The table is keyed by the control's type name in System.Windows.Forms, so the designer reads it from text alone and
/// loads no type.
/// </summary>
internal static class WinFormsControlDefaults
{
    private const string FormsPrefix = "Global.System.Windows.Forms.";

    /// <summary>
    /// The starting values of one type. A null size, dock or flag is not written, so the control keeps its own default.
    /// <see cref="DockedBounds"/> says that a docked control is still written with a Location and Size, as the strips are.
    /// </summary>
    internal sealed record Starting(int? Width = null, int? Height = null, bool Text = false, bool AutoSize = false,
        string? Dock = null, bool TabPages = false, bool DockedBounds = false);

    private static readonly Dictionary<string, Starting> ByType = new(StringComparer.Ordinal)
    {
        ["Button"] = new(75, 23, Text: true),
        ["TextBox"] = new(100, 23),
        ["MaskedTextBox"] = new(100, 23),
        ["RichTextBox"] = new(100, 96),
        ["Label"] = new(Text: true, AutoSize: true),
        ["CheckBox"] = new(Text: true, AutoSize: true),
        ["RadioButton"] = new(Text: true, AutoSize: true),
        ["ComboBox"] = new(121, 23),
        ["ListBox"] = new(120, 94),
        ["ListView"] = new(121, 97),
        ["TreeView"] = new(121, 97),
        ["NumericUpDown"] = new(120, 23),
        ["DateTimePicker"] = new(200, 23),
        ["ProgressBar"] = new(100, 23),
        ["PictureBox"] = new(100, 50),
        ["WebBrowser"] = new(250, 250),
        ["Panel"] = new(200, 100),
        ["FlowLayoutPanel"] = new(200, 100),
        ["TableLayoutPanel"] = new(200, 100),
        ["GroupBox"] = new(200, 100, Text: true),
        ["DataGridView"] = new(240, 150),
        ["TabControl"] = new(200, 100, TabPages: true),
        ["SplitContainer"] = new(150, 100),
        // A strip's width is the form's client width, so only its height is starting data.
        ["MenuStrip"] = new(Height: 24, Dock: "Top", DockedBounds: true),
        ["ToolStrip"] = new(Height: 25, Dock: "Top", DockedBounds: true),
        ["StatusStrip"] = new(Height: 22, Dock: "Bottom", DockedBounds: true),
    };

    /// <summary>The starting values for a control of the given type name; an unlisted type starts with none of them.</summary>
    public static Starting For(string typeName) =>
        typeName.StartsWith(FormsPrefix, StringComparison.Ordinal)
            && ByType.TryGetValue(typeName[FormsPrefix.Length..], out var starting)
            ? starting
            : new Starting();
}
