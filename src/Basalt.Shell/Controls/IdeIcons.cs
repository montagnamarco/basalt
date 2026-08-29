using Avalonia;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>What an icon depicts.</summary>
public enum IconKind
{
    None,
    Solution, Project, Folder, FolderOpen,
    VisualBasicFile, CSharpFile, RazorFile, WebFile, StyleFile, ScriptFile,
    XmlFile, JsonFile, TextFile, ImageFile, DesignerFile,
    New, Open, Save, SaveAll, Undo, Redo, Cut, Copy, Paste,
    Find, Replace, Build, Run, Stop, Debug, StepOver, StepInto, StepOut,
    Continue, Pause,
    Breakpoint, Settings, Terminal, Branch, Commit, Refresh,
    Error, Warning, Information,

    // Drawn for the menu, which had entries with no icon beside neighbours
    // that had one.
    Exit, GoToDefinition, FindReferences, GoToSymbol, Back, Forward,
    Explorer, Toolbox, Properties, Problems, Output, Outline, References,
    Tests, History, Pull, Push, Assistant, Layout, Window, Minimize, Zoom,

    /// <summary>The controls Dock draws on a panel's title bar.</summary>
    Close, Pin, Menu,

    /// <summary>Two versions of a file, side by side.</summary>
    Diff,

    /// <summary>Basalt itself: a volcano, which is where basalt comes from.</summary>
    Application
}

/// <summary>
/// The icons the interface uses, drawn as vector geometry.
///
/// Drawn rather than shipped as files, for the same reasons the completion
/// icons are: no asset pipeline, no licensing to track, and they scale to any
/// size and follow the theme's foreground colour. The shapes follow the
/// conventions of Visual Studio and Visual Studio Code closely enough to be
/// recognised without being copies.
/// </summary>
public static class IdeIcons
{
    /// <summary>
    /// The outline of an icon, or null when there is nothing to draw.
    ///
    /// Every path is drawn in a 16 by 16 box, so a caller can scale one
    /// number and have every icon agree.
    /// </summary>
    public static Geometry? PathFor(IconKind kind)
    {
        var data = Data(kind);

        return data is null ? null : Geometry.Parse(data);
    }

    /// <summary>
    /// The outline as path data, before it becomes geometry.
    ///
    /// Parsing one needs a render backend, which a plain unit test has no
    /// reason to start; this says which kinds have a shape without one.
    /// </summary>
    public static string? PathDataFor(IconKind kind) => Data(kind);

    /// <summary>The colour an icon is drawn in, where it carries meaning.</summary>
    public static Color? AccentFor(IconKind kind) => kind switch
    {
        IconKind.VisualBasicFile => Color.FromRgb(0x00, 0x69, 0xC0),
        IconKind.CSharpFile => Color.FromRgb(0x68, 0x21, 0x7A),
        IconKind.RazorFile => Color.FromRgb(0x51, 0x2B, 0xD4),
        IconKind.WebFile => Color.FromRgb(0xE4, 0x4D, 0x26),
        IconKind.StyleFile => Color.FromRgb(0x26, 0x4D, 0xE4),
        IconKind.ScriptFile => Color.FromRgb(0xD6, 0xA5, 0x00),
        IconKind.JsonFile => Color.FromRgb(0x8A, 0x8A, 0x00),
        IconKind.Solution => Color.FromRgb(0x68, 0x21, 0x7A),
        IconKind.Project => Color.FromRgb(0x00, 0x69, 0xC0),
        IconKind.Folder or IconKind.FolderOpen => Color.FromRgb(0xDC, 0xB6, 0x7A),
        IconKind.Run or IconKind.Continue => Color.FromRgb(0x2E, 0xA0, 0x43),
        IconKind.Stop or IconKind.Error or IconKind.Breakpoint => Color.FromRgb(0xE5, 0x14, 0x00),
        IconKind.Warning => Color.FromRgb(0xBF, 0x87, 0x00),
        IconKind.Information => Color.FromRgb(0x37, 0x94, 0xFF),
        IconKind.Branch or IconKind.Commit => Color.FromRgb(0x2E, 0xA0, 0x43),

        // Basalt is dark rock; the lava is what gives it colour.
        IconKind.Application => Color.FromRgb(0xD9, 0x53, 0x1E),
        _ => null
    };

    /// <summary>The icon a file's extension calls for.</summary>
    public static IconKind ForFile(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".vb" or ".bas" => IconKind.VisualBasicFile,
            ".cs" => IconKind.CSharpFile,
            ".vbhtml" or ".cshtml" or ".razor" => IconKind.RazorFile,
            ".html" or ".htm" => IconKind.WebFile,
            ".css" or ".scss" or ".less" => IconKind.StyleFile,
            ".js" or ".mjs" or ".ts" => IconKind.ScriptFile,
            ".json" => IconKind.JsonFile,
            ".xml" or ".config" or ".props" or ".targets"
                or ".csproj" or ".vbproj" or ".slnx" => IconKind.XmlFile,
            ".axaml" or ".xaml" => IconKind.DesignerFile,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".ico" => IconKind.ImageFile,
            _ => IconKind.TextFile
        };

    /// <summary>
    /// The geometry of each icon, in a 16 by 16 box.
    ///
    /// Written out rather than composed from primitives: the shapes are read
    /// far more often than they are changed, and a path is the clearest way to
    /// say what one looks like.
    /// </summary>
    private static string? Data(IconKind kind) => kind switch
    {
        // A document with a folded corner is the base for every file icon;
        // the accent colour is what distinguishes them.
        IconKind.TextFile or IconKind.VisualBasicFile or IconKind.CSharpFile
            or IconKind.RazorFile or IconKind.WebFile or IconKind.StyleFile
            or IconKind.ScriptFile or IconKind.JsonFile or IconKind.XmlFile
            or IconKind.ImageFile or IconKind.DesignerFile =>
            "M3,1 H10 L13,4 V15 H3 Z M10,1 V4 H13",

        IconKind.Folder => "M1,3 H6 L7.5,5 H15 V13 H1 Z",
        IconKind.FolderOpen => "M1,3 H6 L7.5,5 H15 V6 H3 L1,13 Z M3,6 H15 L13,13 H1 Z",

        IconKind.Solution => "M8,1 L15,5 V11 L8,15 L1,11 V5 Z M8,1 V8 M8,8 L15,5 M8,8 L1,5",
        IconKind.Project => "M2,2 H14 V14 H2 Z M2,5 H14",

        IconKind.New => "M3,1 H10 L13,4 V15 H3 Z M8,7 V12 M5.5,9.5 H10.5",
        IconKind.Open => "M1,4 H6 L7.5,6 H15 V13 H1 Z",
        IconKind.Save => "M2,2 H12 L14,4 V14 H2 Z M5,2 V6 H11 V2 M4,9 H12 V14 H4 Z",
        IconKind.SaveAll => "M1,1 H10 L12,3 V12 H1 Z M4,4 H15 V15 H4 Z",

        IconKind.Undo => "M6,4 L2,7 L6,10 M2,7 H10 A4,4 0 0 1 10,15",
        IconKind.Redo => "M10,4 L14,7 L10,10 M14,7 H6 A4,4 0 0 0 6,15",

        IconKind.Cut => "M4,1 L12,13 M12,1 L4,13 M3,14 A2,2 0 1 0 3,10 A2,2 0 0 0 3,14 "
                      + "M13,14 A2,2 0 1 1 13,10 A2,2 0 0 1 13,14",
        IconKind.Copy => "M2,2 H10 V10 H2 Z M6,6 H14 V14 H6 Z",
        IconKind.Paste => "M4,2 H12 V15 H4 Z M6,1 H10 V3 H6 Z",

        IconKind.Find => "M7,2 A5,5 0 1 1 7,12 A5,5 0 0 1 7,2 M11,11 L15,15",
        IconKind.Replace => "M6,2 A4,4 0 1 1 6,10 A4,4 0 0 1 6,2 M9,9 L12,12 M10,14 H15 M13,12 L15,14 L13,16",

        IconKind.Build => "M3,10 L8,2 L13,10 Z M2,12 H14 V14 H2 Z",
        IconKind.Run => "M4,2 L13,8 L4,14 Z",

        // Continue is Run's triangle against a bar, the way a player draws
        // "resume"; Pause is the two bars on their own.
        IconKind.Continue => "M3,3 V13 M6,3 L14,8 L6,13 Z",
        IconKind.Pause => "M4,3 H6.5 V13 H4 Z M9.5,3 H12 V13 H9.5 Z",
        IconKind.Stop => "M3,3 H13 V13 H3 Z",

        IconKind.Debug => "M5,4 A3,3 0 0 1 11,4 M4,6 H12 V11 A4,4 0 0 1 4,11 Z "
                        + "M1,7 H4 M12,7 H15 M1,12 H4 M12,12 H15",

        IconKind.StepOver => "M2,10 A6,6 0 0 1 14,10 M14,10 V6 M14,10 H10 M8,13 A1.5,1.5 0 1 0 8,16",
        IconKind.StepInto => "M8,2 V9 M5,6.5 L8,9.5 L11,6.5 M8,13 A1.5,1.5 0 1 0 8,16",
        IconKind.StepOut => "M8,10 V3 M5,6.5 L8,3.5 L11,6.5 M8,13 A1.5,1.5 0 1 0 8,16",

        IconKind.Breakpoint => "M8,3 A5,5 0 1 1 8,13 A5,5 0 0 1 8,3",

        // A door with the way out marked.
        IconKind.Exit => "M9,2 H3 V14 H9 M6,8 H14 M11.5,5.5 L14,8 L11.5,10.5",

        // An arrow into a bracket: going to where something is declared.
        IconKind.GoToDefinition => "M2,8 H9 M6.5,5 L9.5,8 L6.5,11 M12,3 V13 H14 M12,3 H14",

        // Several marks around one thing: the places it is used.
        IconKind.FindReferences => "M8,5.5 A2.5,2.5 0 1 1 8,10.5 A2.5,2.5 0 0 1 8,5.5 "
                                 + "M2,3 H5 M2,8 H4 M2,13 H5 M11,3 H14 M12,8 H14 M11,13 H14",

        // A magnifier over a list: finding a name among many.
        IconKind.GoToSymbol => "M6.5,3 A3.5,3.5 0 1 1 6.5,10 A3.5,3.5 0 0 1 6.5,3 "
                             + "M9.2,9.2 L13,13 M2,13 H7",

        IconKind.Back => "M13,8 H3 M6.5,4.5 L3,8 L6.5,11.5",
        IconKind.Forward => "M3,8 H13 M9.5,4.5 L13,8 L9.5,11.5",

        // A tree, which is what the solution explorer shows.
        IconKind.Explorer => "M2,3 H6 M2,7 H5 M2,11 H6 M8,3 H14 M8,7 H14 M8,11 H14",

        // A box of parts to place.
        IconKind.Toolbox => "M2,6 H14 V13 H2 Z M5.5,6 V4 H10.5 V6 M2,9 H14",

        // A list of names and values.
        IconKind.Properties => "M2,4 H6 M8,4 H14 M2,8 H6 M8,8 H14 M2,12 H6 M8,12 H14",

        // A triangle with a mark: something to attend to.
        IconKind.Problems => "M8,2.5 L14,13 H2 Z M8,6.5 V9.5 M8,11 V11.5",

        // Lines of text produced by something running.
        IconKind.Output => "M2,4 H14 M2,7 H11 M2,10 H13 M2,13 H8",

        // Nested headings.
        IconKind.Outline => "M2,3 H14 M4,7 H14 M6,11 H14 M2,3 V11 M4,7 V11",

        // Arrows meeting one point.
        IconKind.References => "M8,3 V13 M4,5 L8,8 M12,5 L8,8 M4,11 L8,8 M12,11 L8,8",

        // A tick inside a frame: a test that passed.
        IconKind.Tests => "M2.5,2.5 H13.5 V13.5 H2.5 Z M5,8 L7,10.5 L11,5.5",

        // A clock, which is what a history is.
        IconKind.History => "M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 "
                          + "M8,5 V8 L10.5,9.5",

        IconKind.Pull => "M8,2 V10 M4.5,6.5 L8,10 L11.5,6.5 M3,13 H13",
        IconKind.Push => "M8,10 V2 M4.5,5.5 L8,2 L11.5,5.5 M3,13 H13",

        // A speech bubble: something that answers.
        IconKind.Assistant => "M2.5,3 H13.5 V10 H7 L4,13 V10 H2.5 Z",

        // Panes: what a layout arranges.
        IconKind.Layout => "M2,2.5 H14 V13.5 H2 Z M6.5,2.5 V13.5 M6.5,8 H14",

        IconKind.Window => "M2,3 H14 V13 H2 Z M2,6 H14",
        IconKind.Minimize => "M3,12 H13",

        // The three on a panel's title bar. Dock names the buttons but
        // its theme draws nothing in them, which leaves grey squares.
        IconKind.Close => "M4,4 L12,12 M12,4 L4,12",
        IconKind.Pin => "M9.5,2 L14,6.5 L11.5,7.5 L9,13 L3,7 L8.5,4.5 Z M3,13 L6,10",
        IconKind.Menu => "M3,4.5 H13 M3,8 H13 M3,11.5 H13",

        // Two panes with a line between: what a diff looks like before
        // you can read any of it.
        IconKind.Diff => "M1.5,2.5 H7 V13.5 H1.5 Z M9,2.5 H14.5 V13.5 H9 Z M3,5.5 H5.5 M3,8 H5.5 M10.5,8 H13 M10.5,10.5 H13",
        IconKind.Zoom => "M3,3 H13 V13 H3 Z M6,6 H10 V10 H6 Z",

        // Basalt: the rock the IDE is named after, which cools into hexagonal
        // columns. The same two-column figure as basalt-small.svg, so the
        // window, the dock and the About box cannot show different icons.
        //
        // Two rather than the three in the full artwork: rendered at sixteen
        // pixels three of them merge into one smudge with no shape in it, which
        // is only visible by rasterising and looking.
        IconKind.Application =>
            // The shorter column: its hexagonal top, then the shaft.
            "M3.08,8.11 L5.25,8.72 L7.42,8.11 L7.42,6.89 L5.25,6.28 L3.08,6.89 Z "
          + "M3.08,8.11 L3.08,6.89 L5.25,6.28 L7.42,6.89 L7.42,8.11 L7.42,12.50 L3.08,12.50 Z "
            // The taller one, in front.
          + "M8.08,5.61 L10.25,6.22 L12.42,5.61 L12.42,4.39 L10.25,3.77 L8.08,4.39 Z "
          + "M8.08,5.61 L8.08,4.39 L10.25,3.77 L12.42,4.39 L12.42,5.61 L12.42,12.50 L8.08,12.50 Z",

        IconKind.Settings => "M8,5.5 A2.5,2.5 0 1 1 8,10.5 A2.5,2.5 0 0 1 8,5.5 "
                           + "M8,1 V3 M8,13 V15 M1,8 H3 M13,8 H15 "
                           + "M3,3 L4.5,4.5 M11.5,11.5 L13,13 M13,3 L11.5,4.5 M4.5,11.5 L3,13",

        IconKind.Terminal => "M1,2 H15 V14 H1 Z M3.5,5.5 L6.5,8 L3.5,10.5 M8,11 H12",

        IconKind.Branch => "M4,3 A1.5,1.5 0 1 1 4,6 A1.5,1.5 0 0 1 4,3 M4,6 V13 "
                         + "M12,3 A1.5,1.5 0 1 1 12,6 A1.5,1.5 0 0 1 12,3 "
                         + "M12,6 V8 A2,2 0 0 1 10,10 H4 M4,13 A1.5,1.5 0 1 0 4,16",

        IconKind.Commit => "M1,8 H5 M11,8 H15 M8,5 A3,3 0 1 1 8,11 A3,3 0 0 1 8,5",
        IconKind.Refresh => "M13,4 A6,6 0 1 0 14,9 M13,1 V4 H10",

        IconKind.Error => "M8,1.5 A6.5,6.5 0 1 1 8,14.5 A6.5,6.5 0 0 1 8,1.5 M5.5,5.5 L10.5,10.5 M10.5,5.5 L5.5,10.5",
        IconKind.Warning => "M8,2 L15,14 H1 Z M8,6 V10 M8,11.5 V12.5",
        IconKind.Information => "M8,1.5 A6.5,6.5 0 1 1 8,14.5 A6.5,6.5 0 0 1 8,1.5 M8,4 V5 M8,7 V12",

        _ => null
    };
}
