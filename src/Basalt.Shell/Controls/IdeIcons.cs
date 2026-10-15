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

    // Drawn so the View menu's debug-layout tools and Project's Add
    // Reference stop sharing an icon with an unrelated command (see
    // MenuTests.NoIconStandsForTwoUnrelatedCommands).
    CallStack, Variables, BreakpointsWindow, AddReference,

    /// <summary>The controls Dock draws on a panel's title bar.</summary>
    Close, Pin, Menu,

    /// <summary>Two versions of a file, side by side.</summary>
    Diff,

    /// <summary>A prompt and its cursor: the Immediate window.</summary>
    Immediate,

    /// <summary>A box opened to show what is inside: the Object Browser.</summary>
    ObjectBrowser,

    /// <summary>A tray with an arrow leaving it: making a program to hand out.</summary>
    Publish,

    /// <summary>An eye: the Watch window, which keeps an eye on expressions.</summary>
    Watch,

    /// <summary>Folded and unfolded: the state of a category in a list.</summary>
    ChevronRight, ChevronDown,

    /// <summary>Basalt itself: a volcano, which is where basalt comes from.</summary>
    Application,

    /// <summary>
    /// The Database Explorer's tree. A connection wears the cylinder of a
    /// database with a small mark for its engine; the rest name what a node
    /// is, so the tree reads at a glance.
    /// </summary>
    DatabaseSqlite, DatabaseSqlServer, DatabasePostgreSql, DatabaseMySql,
    FolderTables, FolderViews,
    Table, View, Column, PrimaryKeyColumn, ForeignKeyColumn, Index,

    /// <summary>The database views' buttons: a database with a plus, a plug in and out, first and last page, a row added or removed.</summary>
    DatabaseAdd, Connect, Disconnect, PageFirst, PageLast, RowAdd, RowDelete
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
    /// Whether an icon is drawn filled rather than outlined. Only the
    /// application's own: its honeycomb cells are too small to outline at
    /// sixteen pixels without running together.
    /// </summary>
    public static bool IsFilled(IconKind kind) => kind == IconKind.Application;

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

        // The database icons. Each sits between a luminance of 0.14 and 0.30,
        // which keeps it at least 3:1 against both a white panel and a dark
        // one (DatabaseIconTests checks it), so one colour serves both themes.
        IconKind.DatabaseSqlite => Color.FromRgb(0x3D, 0x8F, 0xC9),
        IconKind.DatabaseSqlServer => Color.FromRgb(0xC2, 0x4A, 0x45),
        IconKind.DatabasePostgreSql => Color.FromRgb(0x4B, 0x7F, 0xB5),
        IconKind.DatabaseMySql => Color.FromRgb(0xC2, 0x7A, 0x1E),
        IconKind.FolderTables or IconKind.FolderViews => Color.FromRgb(0xB7, 0x8A, 0x3A),
        IconKind.Table => Color.FromRgb(0x4A, 0x86, 0xC5),
        IconKind.View => Color.FromRgb(0x2F, 0x9A, 0x8E),
        IconKind.Column => Color.FromRgb(0x6F, 0x8F, 0xB8),
        IconKind.PrimaryKeyColumn => Color.FromRgb(0xB7, 0x8A, 0x3A),
        IconKind.ForeignKeyColumn => Color.FromRgb(0x8A, 0x6F, 0xC9),
        IconKind.Index => Color.FromRgb(0x2F, 0x9A, 0x8E),

        // Basalt is dark rock; the lava is what gives it colour.
        IconKind.Application => Color.FromRgb(0xE4, 0x57, 0x2E),
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
    /// <summary>The cylinder under a connection's engine mark: shorter than a database's, to leave room for the mark.</summary>
    private const string ConnectionBase =
        "M2,3.5 A5,1.75 0 1 1 12,3.5 A5,1.75 0 1 1 2,3.5 Z M2,3.5 V10 A5,1.75 0 0 0 8.5,11.75 M2,6.75 A5,1.75 0 0 0 12,6.75 M12,3.5 V7 ";

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
        IconKind.ObjectBrowser => "M8,2 L14,5 V11 L8,14 L2,11 V5 Z M2,5 L8,8 L14,5 M8,8 V14",
        IconKind.Publish => "M2,9 V13.5 H14 V9 M8,2 V10 M5,5 L8,2 L11,5",
        IconKind.Watch => "M1.5,8 C4,3.5 12,3.5 14.5,8 C12,12.5 4,12.5 1.5,8 Z M8,6 A2,2 0 1 1 8,10 A2,2 0 1 1 8,6 Z",

        IconKind.Diff => "M1.5,2.5 H7 V13.5 H1.5 Z M9,2.5 H14.5 V13.5 H9 Z M3,5.5 H5.5 M3,8 H5.5 M10.5,8 H13 M10.5,10.5 H13",
        IconKind.ChevronRight => "M6,3.5 L10.5,8 L6,12.5",
        IconKind.ChevronDown => "M3.5,6 L8,10.5 L12.5,6",
        IconKind.Zoom => "M3,3 H13 V13 H3 Z M6,6 H10 V10 H6 Z",

        // Basalt: the rock the IDE is named after. Cooling lava cracks into
        // hexagonal columns, whose tops fit together as a honeycomb: seven of
        // them, as in basalt.svg, the same icon in the window, the dock and
        // the About box. Filled, not stroked (see IsFilled): outlined at
        // sixteen pixels the cells run into one another.
        //
        // The proportions of basalt-small.svg, a quarter size, with a gap of
        // a whole unit between cells, which is what keeps them apart on
        // screen; checked by rasterising, in ApplicationIconTests.
        IconKind.Application =>
            "M8,5.9 L9.82,6.95 L9.82,9.05 L8,10.1 L6.18,9.05 L6.18,6.95 Z "
          + "M12.64,5.9 L14.46,6.95 L14.46,9.05 L12.64,10.1 L10.82,9.05 L10.82,6.95 Z "
          + "M10.32,9.92 L12.14,10.97 L12.14,13.07 L10.32,14.12 L8.5,13.07 L8.5,10.97 Z "
          + "M5.68,9.92 L7.5,10.97 L7.5,13.07 L5.68,14.12 L3.86,13.07 L3.86,10.97 Z "
          + "M3.36,5.9 L5.18,6.95 L5.18,9.05 L3.36,10.1 L1.54,9.05 L1.54,6.95 Z "
          + "M5.68,1.88 L7.5,2.93 L7.5,5.03 L5.68,6.08 L3.86,5.03 L3.86,2.93 Z "
          + "M10.32,1.88 L12.14,2.93 L12.14,5.03 L10.32,6.08 L8.5,5.03 L8.5,2.93 Z",

        IconKind.Settings => "M8,5.5 A2.5,2.5 0 1 1 8,10.5 A2.5,2.5 0 0 1 8,5.5 "
                           + "M8,1 V3 M8,13 V15 M1,8 H3 M13,8 H15 "
                           + "M3,3 L4.5,4.5 M11.5,11.5 L13,13 M13,3 L11.5,4.5 M4.5,11.5 L3,13",

        IconKind.Immediate => "M2,3.5 L7.5,8 L2,12.5 M9,12.5 H14",

        // Stacked frames with an arrow pointing at the one on top: the Call
        // Stack window, which is exactly that — frames, newest first.
        IconKind.CallStack => "M2,11 H14 M2,8 H14 M2,5 H10 M11.5,3 V7 M9.5,5 L11.5,3 L13.5,5",

        // "{x}": a variable's name inside braces, the shorthand this window
        // is named for in Visual Studio and Code alike.
        IconKind.Variables => "M4,2 C2.5,2 2.5,4 2.5,5.5 C2.5,7 1,7 1,8 C1,9 2.5,9 2.5,10.5 "
                            + "C2.5,12 2.5,14 4,14 M12,2 C13.5,2 13.5,4 13.5,5.5 C13.5,7 15,7 15,8 "
                            + "C15,9 13.5,9 13.5,10.5 C13.5,12 13.5,14 12,14 M7,8 H9",

        // A filled dot beside lines of a list: the Breakpoints window, which
        // lists every breakpoint rather than marking one in the margin (see
        // Breakpoint, the toggle's own icon).
        IconKind.BreakpointsWindow => "M2.5,4 A1.5,1.5 0 1 1 2.5,7 A1.5,1.5 0 0 1 2.5,4 Z "
                                    + "M6,4.5 H14 M6,8.5 H14 M2.5,11 H14",

        // An open book with a plus: adding a reference to the project reads
        // another assembly, the way a book lends its own words.
        IconKind.AddReference => "M2,3 C4,2 6,2.5 8,3.5 C10,2.5 12,2 14,3 V12 "
                               + "C12,11 10,11.5 8,12.5 C6,11.5 4,11 2,12 Z M8,3.5 V12.5 "
                               + "M11.5,5 V8 M10,6.5 H13",
        IconKind.Terminal => "M1,2 H15 V14 H1 Z M3.5,5.5 L6.5,8 L3.5,10.5 M8,11 H12",

        IconKind.Branch => "M4,3 A1.5,1.5 0 1 1 4,6 A1.5,1.5 0 0 1 4,3 M4,6 V13 "
                         + "M12,3 A1.5,1.5 0 1 1 12,6 A1.5,1.5 0 0 1 12,3 "
                         + "M12,6 V8 A2,2 0 0 1 10,10 H4 M4,13 A1.5,1.5 0 1 0 4,16",

        IconKind.Commit => "M1,8 H5 M11,8 H15 M8,5 A3,3 0 1 1 8,11 A3,3 0 0 1 8,5",
        IconKind.Refresh => "M13,4 A6,6 0 1 0 14,9 M13,1 V4 H10",

        // The cylinder of a database, and the same drawn shorter above a mark
        // for the engine: SQLite a page (the file it is), SQL Server a
        // diamond, PostgreSQL a ring, MySQL a triangle.
        IconKind.DatabaseSqlite => ConnectionBase + "M10.5,9.5 H15 V15 H10.5 Z",
        IconKind.DatabaseSqlServer => ConnectionBase + "M12.75,9.25 L15,12 L12.75,14.75 L10.5,12 Z",
        IconKind.DatabasePostgreSql => ConnectionBase + "M12.75,9.75 A2.25,2.25 0 1 1 12.75,14.25 A2.25,2.25 0 0 1 12.75,9.75",
        IconKind.DatabaseMySql => ConnectionBase + "M12.75,9.5 L15,14.75 H10.5 Z",

        // A folder with the shape of what it holds inside: a grid of cells,
        // an eye, a block that runs, an f.
        IconKind.FolderTables => "M1,3 H6 L7.5,5 H15 V13 H1 Z M4,8 H12 M4,10.5 H12 M7,6.5 V13",
        IconKind.FolderViews => "M1,3 H6 L7.5,5 H15 V13 H1 Z M3.5,9.5 C5,7 11,7 12.5,9.5 C11,12 5,12 3.5,9.5 Z M8,8.75 V10.25",

        // A table is a grid; a view is the same grid seen through an eye.
        IconKind.Table => "M2,3 H14 V13 H2 Z M2,6.5 H14 M2,9.75 H14 M6.5,3 V13",
        IconKind.View => "M2,2.5 H14 V13.5 H2 Z M2,5.5 H14 M4.5,9.75 C6,7.75 10,7.75 11.5,9.75 C10,11.75 6,11.75 4.5,9.75 Z M7.4,9.75 A0.6,0.6 0 1 1 8.6,9.75 A0.6,0.6 0 1 1 7.4,9.75",

        // A column is one stripe of the grid; a key marks the one that
        // identifies a row, a chain link the one that points at another table.
        IconKind.Column => "M5,2.5 H11 V13.5 H5 Z M5,5.5 H11",
        IconKind.PrimaryKeyColumn => "M4.5,4.5 A2.75,2.75 0 1 1 4.5,10 A2.75,2.75 0 0 1 4.5,4.5 M7.25,7.25 H14 M11.5,7.25 V10 M14,7.25 V9.5",
        IconKind.ForeignKeyColumn => "M6.5,5 H5 A3,3 0 0 0 5,11 H6.5 M9.5,5 H11 A3,3 0 0 1 11,11 H9.5 M6,8 H10",

        // Lines in order, with the pointer that finds one: an index.
        IconKind.Index => "M2,3.5 H9 M2,7 H7 M2,10.5 H9 M2,14 H7 M12,3 V13 M10,11 L12,13 L14,11",

        // A block that runs, and an f of the kind a mathematician writes.

        // The buttons of the database views.
        IconKind.DatabaseAdd => "M2,3.5 A5,1.75 0 1 1 12,3.5 A5,1.75 0 1 1 2,3.5 Z M2,3.5 V10 A5,1.75 0 0 0 7,11.75 "
                              + "M2,6.75 A5,1.75 0 0 0 12,6.75 M12,3.5 V7 M12.5,9 V15 M9.5,12 H15.5",
        IconKind.Connect => "M5.5,2 V5.5 M10.5,2 V5.5 M3.5,5.5 H12.5 V8 A4.5,3.5 0 0 1 3.5,8 Z M8,11.5 V14.5",
        IconKind.Disconnect => "M5.5,2 V5.5 M10.5,2 V5.5 M3.5,5.5 H12.5 V8 A4.5,3.5 0 0 1 3.5,8 Z M8,11.5 V14.5 M2,14 L14,2",
        IconKind.PageFirst => "M3,3 V13 M13,3 L6,8 L13,13 Z",
        IconKind.PageLast => "M13,3 V13 M3,3 L10,8 L3,13 Z",
        IconKind.RowAdd => "M2,3.5 H14 V8 H2 Z M8,10 V15 M5.5,12.5 H10.5",
        IconKind.RowDelete => "M2,3.5 H14 V8 H2 Z M5.5,12.5 H10.5",

        IconKind.Error => "M8,1.5 A6.5,6.5 0 1 1 8,14.5 A6.5,6.5 0 0 1 8,1.5 M5.5,5.5 L10.5,10.5 M10.5,5.5 L5.5,10.5",
        IconKind.Warning => "M8,2 L15,14 H1 Z M8,6 V10 M8,11.5 V12.5",
        IconKind.Information => "M8,1.5 A6.5,6.5 0 1 1 8,14.5 A6.5,6.5 0 0 1 8,1.5 M8,4 V5 M8,7 V12",

        _ => null
    };
}
