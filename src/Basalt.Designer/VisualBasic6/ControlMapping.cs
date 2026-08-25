namespace Basalt.Designer.VisualBasic6;

/// <summary>How closely a VB6 control can be reproduced.</summary>
public enum MappingFidelity
{
    /// <summary>Avalonia has the same control, and the properties carry across.</summary>
    Direct,

    /// <summary>It can be built, but out of parts rather than one control.</summary>
    Composed,

    /// <summary>Nothing here answers to it; a form using it loses something.</summary>
    Unsupported
}

/// <summary>What one VB6 control becomes.</summary>
/// <param name="Vb6">The VB6 type name, as the .frm writes it.</param>
/// <param name="Avalonia">The Avalonia control, or null when there is none.</param>
/// <param name="Note">Why, where the answer is not obvious.</param>
public sealed record ControlMapping(
    string Vb6,
    string? Avalonia,
    MappingFidelity Fidelity,
    string Note = "");

/// <summary>
/// What the VB6 intrinsic controls become in Avalonia.
///
/// Written by going through the intrinsic toolbox one control at a time, and
/// saying plainly where nothing answers. The ones marked Unsupported are the
/// honest part: a compatibility claim that quietly drops OLE or the shape
/// control would be worse than one that names them.
///
/// The intrinsic controls are the ones in the VB6 toolbox without adding a
/// component. Anything from an .ocx is out of scope entirely and is not
/// listed, since supporting one would mean supporting a binary for a platform
/// this IDE does not run on.
/// </summary>
public static class ControlMappings
{
    public static IReadOnlyList<ControlMapping> All { get; } =
    [
        new("VB.Form", "Window", MappingFidelity.Direct,
            "Caption becomes Title; ClientWidth and ClientHeight are the content size."),

        new("VB.CommandButton", "Button", MappingFidelity.Direct),
        new("VB.Label", "TextBlock", MappingFidelity.Direct),
        new("VB.TextBox", "TextBox", MappingFidelity.Direct,
            "MultiLine becomes AcceptsReturn; PasswordChar becomes PasswordChar."),
        new("VB.CheckBox", "CheckBox", MappingFidelity.Direct,
            "VB6 has three states through Value = 2, which IsThreeState covers."),
        new("VB.OptionButton", "RadioButton", MappingFidelity.Direct,
            "Grouping is by container in both, so a Frame becomes the group."),
        new("VB.ComboBox", "ComboBox", MappingFidelity.Direct,
            "Style 0 is editable, which is IsEditable."),
        new("VB.ListBox", "ListBox", MappingFidelity.Direct),
        new("VB.Frame", "HeaderedContentControl", MappingFidelity.Direct,
            "A GroupBox in all but name; Caption becomes the header."),
        new("VB.PictureBox", "Border", MappingFidelity.Composed,
            "A container that can also draw: a Border holding an Image covers "
          + "the common use, but PictureBox is also a drawing surface with its "
          + "own graphics methods, which a Border is not."),

        new("VB.HScrollBar", "ScrollBar", MappingFidelity.Direct,
            "Orientation says which; VB6 has a type for each."),
        new("VB.VScrollBar", "ScrollBar", MappingFidelity.Direct),

        new("VB.Timer", "DispatcherTimer", MappingFidelity.Composed,
            "Not a control at all in Avalonia: it is placed on the form in VB6 "
          + "but becomes a field on the window."),

        new("VB.Shape", null, MappingFidelity.Unsupported,
            "A rectangle, oval or rounded rectangle drawn on the form. Avalonia "
          + "has the shapes, but VB6's is a design-time drawing primitive with "
          + "no direct equivalent as a placed control."),

        new("VB.Line", null, MappingFidelity.Unsupported,
            "The same: a drawn line rather than a control."),

        new("VB.Image", "Image", MappingFidelity.Direct,
            "The lighter of VB6's two picture controls, and the closer match."),

        new("VB.Data", null, MappingFidelity.Unsupported,
            "Bound to a Jet database through DAO. Nothing here answers to it, "
          + "and reproducing it would mean reproducing DAO."),

        new("VB.OLE", null, MappingFidelity.Unsupported,
            "Embeds an OLE object, which is a Windows COM facility. Out of "
          + "reach on macOS and Linux by construction."),

        new("VB.DriveListBox", null, MappingFidelity.Unsupported,
            "Lists drive letters, which only Windows has."),
        new("VB.DirListBox", "TreeView", MappingFidelity.Composed,
            "A directory tree can be built, but not as one control."),
        new("VB.FileListBox", "ListBox", MappingFidelity.Composed,
            "The same: a list bound to a directory's files.")
    ];

    /// <summary>What a VB6 type becomes, or null when it is not one we know.</summary>
    public static ControlMapping? For(string vb6TypeName) =>
        All.FirstOrDefault(m => m.Vb6.Equals(vb6TypeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The controls that carry across as themselves.</summary>
    public static IReadOnlyList<ControlMapping> Direct =>
        [.. All.Where(m => m.Fidelity == MappingFidelity.Direct)];

    /// <summary>The controls nothing here answers to.</summary>
    public static IReadOnlyList<ControlMapping> Unsupported =>
        [.. All.Where(m => m.Fidelity == MappingFidelity.Unsupported)];

    /// <summary>
    /// What a form would lose, said before it is opened.
    ///
    /// So the designer can warn rather than silently dropping controls, which
    /// is the failure that makes a compatibility claim untrustworthy.
    /// </summary>
    public static IReadOnlyList<string> ProblemsWith(FrmForm form) =>
    [
        .. form.Controls
            .Select(control => (Control: control, Mapping: For(control.TypeName)))
            .Where(pair => pair.Mapping is null
                        || pair.Mapping.Fidelity == MappingFidelity.Unsupported)
            .Select(pair => pair.Mapping is null
                ? $"'{pair.Control.Name}' is a {pair.Control.TypeName}, which is not a "
                  + "control this designer knows: it is probably from an .ocx."
                : $"'{pair.Control.Name}' is a {pair.Control.TypeName}: {pair.Mapping.Note}")
    ];
}
