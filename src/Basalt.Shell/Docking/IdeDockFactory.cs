using System.Collections.ObjectModel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace Basalt.Shell.Docking;

/// <summary>
/// Builds and manages the panel arrangement.
///
/// The initial layout follows VS Code's: solution explorer on the left,
/// documents in the center, toolbox and properties on the right, problems,
/// output and terminal at the bottom. From there the user can move any panel.
/// </summary>
public sealed class IdeDockFactory : Factory
{
    private int _terminaliCreati;

    /// <summary>Central area hosting the document tabs.</summary>
    public DocumentDock? DocumentArea { get; private set; }

    /// <summary>Bottom pane, where new terminals are created.</summary>
    public ToolDock? BottomArea { get; private set; }

    public ToolDock? ToolboxArea { get; private set; }
    public ToolDock? DesignerToolsArea { get; private set; }

    public SolutionExplorerTool SolutionExplorer { get; } = new();
    public ToolboxTool Toolbox { get; } = new();
    public PropertiesTool Properties { get; } = new();
    public ProblemsTool Problems { get; } = new();
    public OutputTool Output { get; } = new();
    public SearchTool Search { get; } = new();
    public OutlineTool Outline { get; } = new();
    public ElementTreeTool ElementTree { get; } = new();
    public DatabaseTool Database { get; } = new();
    public ReferencesTool References { get; } = new();
    public ObjectBrowserTool ObjectBrowser { get; } = new();
    public CallStackTool CallStack { get; } = new();
    public VariablesTool Variables { get; } = new();
    public BreakpointsTool Breakpoints { get; } = new();
    public WatchTool Watch { get; } = new();
    public ImmediateTool Immediate { get; } = new();
    public GitChangesTool GitChanges { get; } = new();
    public GitHistoryTool GitHistory { get; } = new();
    public GitBranchesTool GitBranches { get; } = new();
    public GitDiffTool GitDiff { get; } = new();
    public TestsTool Tests { get; } = new();
    public AssistantTool Assistant { get; } = new();

    /// <summary>Folder in which new terminals are started.</summary>
    public string TerminalWorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

    public override IRootDock CreateLayout()
    {
        DocumentArea = new DocumentDock
        {
            Id = "Documenti",
            Title = "Documenti",
            IsCollapsable = false,
            CanCreateDocument = false,
            VisibleDockables = CreateList<IDockable>()
        };

        // Laid out as Visual Basic 6 was, because that is the arrangement the
        // people this is for already know: the toolbox down the left, the
        // project tree at the top right with the properties beneath it, and
        // the output along the bottom.
        //
        // The toolbox is narrow. In Visual Basic 6 it is a column of icons
        // beside the form, not a panel competing with it for width.
        var toolboxColumn = new ToolDock
        {
            Id = "Sinistra",
            Title = "Sinistra",
            Alignment = Alignment.Left,
            Proportion = 0.16,
            VisibleDockables = CreateList<IDockable>(Toolbox, ElementTree, Outline),
            ActiveDockable = Toolbox
        };

        ToolboxArea = toolboxColumn;

        // Top right: what is in the solution. Visual Basic 6 called it the
        // Project Explorer and put it here, above the properties.
        var projectPanel = new ToolDock
        {
            Id = "DestraSopra",
            Title = "DestraSopra",
            Alignment = Alignment.Right,
            Proportion = 0.35,
            VisibleDockables = CreateList<IDockable>(SolutionExplorer, GitChanges, GitBranches),
            ActiveDockable = SolutionExplorer
        };

        // Beneath it, the properties of whatever is selected — the pairing
        // that made the Visual Basic 6 designer what it was: pick a control
        // in the tree, set it in the grid below without moving your eyes.
        // Events live inside this same Properties window, as Visual Studio's
        // does: there is no separate Events tool to dock here.
        var propertiesPanel = new ToolDock
        {
            Id = "DestraSotto",
            Title = "DestraSotto",
            Alignment = Alignment.Right,
            Proportion = 0.65,
            VisibleDockables = CreateList<IDockable>(Properties, Assistant),
            ActiveDockable = Properties
        };

        DesignerToolsArea = propertiesPanel;

        var rightColumn = new ProportionalDock
        {
            Id = "Destra",
            Title = "Destra",
            Orientation = Orientation.Vertical,
            Proportion = 0.24,
            VisibleDockables = CreateList<IDockable>(
                projectPanel,
                new ProportionalDockSplitter(),
                propertiesPanel)
        };

        var terminal = CreateTerminal();

        BottomArea = new ToolDock
        {
            Id = "Inferiore",
            Title = "Inferiore",
            Alignment = Alignment.Bottom,
            Proportion = 0.28,
            // Visual Studio keeps this strip small while editing: the Error
            // List, Output and whatever terminal was already running. Search,
            // References, Object Browser, Tests, Database and the Git history
            // and diff panes open here on demand instead of crowding the tab
            // strip from the start (ShowTool reopens them where they were,
            // the way a closed one always comes back). Call Stack, Variables,
            // Watch, Breakpoints and Immediate belong to the debug layout
            // (see EnterDebugLayout) and are not part of the design one.
            VisibleDockables = CreateList<IDockable>(Problems, Output, terminal),
            ActiveDockable = Problems
        };

        // Central column: documents on top, bottom panel below.
        var colonnaCentrale = new ProportionalDock
        {
            Id = "ColonnaCentrale",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                DocumentArea,
                new ProportionalDockSplitter(),
                BottomArea)
        };

        var corpo = new ProportionalDock
        {
            Id = "Corpo",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                toolboxColumn,
                new ProportionalDockSplitter(),
                colonnaCentrale,
                new ProportionalDockSplitter(),
                rightColumn)
        };

        var radice = CreateRootDock();
        radice.Id = "Radice";
        radice.Title = "Radice";
        radice.IsCollapsable = false;
        radice.VisibleDockables = CreateList<IDockable>(corpo);
        radice.ActiveDockable = corpo;
        radice.DefaultDockable = corpo;

        return radice;
    }

    /// <summary>Creates a new terminal and adds it to the bottom pane.</summary>
    public TerminalTool CreateTerminal()
    {
        _terminaliCreati++;
        return new TerminalTool(TerminalWorkingDirectory, _terminaliCreati);
    }

    /// <summary>Opens an additional terminal and brings it to the front.</summary>
    public TerminalTool OpenNewTerminal()
    {
        var terminal = CreateTerminal();

        if (BottomArea is not null)
        {
            AddDockable(BottomArea, terminal);
            SetActiveDockable(terminal);
            SetFocusedDockable(BottomArea, terminal);
        }

        return terminal;
    }

    /// <summary>Shows a panel, reopening it if the user had closed it.</summary>
    public void ShowTool(IDockable pannello, IDock contenitorePredefinito)
    {
        var contenitore = pannello.Owner as IDock ?? contenitorePredefinito;

        if (contenitore.VisibleDockables?.Contains(pannello) != true)
            AddDockable(contenitore, pannello);

        SetActiveDockable(pannello);
        SetFocusedDockable(contenitore, pannello);
    }

    /// <summary>
    /// The panels that only mean something while a program is being
    /// debugged, Visual Studio's "Debug" layout: where execution stopped,
    /// its variables, the expressions being watched, every breakpoint and a
    /// line to evaluate. Nothing here belongs to the "Design" layout the
    /// window otherwise keeps.
    /// </summary>
    private IEnumerable<IdeTool> DebugLayoutTools => [CallStack, Variables, Watch, Breakpoints, Immediate];

    /// <summary>The debug tools this session opened, so stopping closes only those.</summary>
    private readonly HashSet<IDockable> _openedForDebugLayout = [];

    /// <summary>
    /// Switches the bottom strip to the debug layout as a session starts.
    ///
    /// A tool the user already had open - in the bottom strip or moved
    /// somewhere else entirely - is left exactly where it is: only the ones
    /// this call itself adds are remembered, so stopping later undoes this
    /// and nothing the user did by hand.
    /// </summary>
    public void EnterDebugLayout()
    {
        if (BottomArea is null) return;

        foreach (var tool in DebugLayoutTools)
        {
            if (tool.Owner is not null) continue;

            ShowTool(tool, BottomArea);
            _openedForDebugLayout.Add(tool);
        }

        SetActiveDockable(CallStack);
        SetFocusedDockable(BottomArea, CallStack);
    }

    /// <summary>
    /// Takes the debug layout away as a session ends, restoring the design
    /// one it replaced.
    ///
    /// Only closes what <see cref="EnterDebugLayout"/> itself opened: one the
    /// user had already pinned open, in the bottom strip or elsewhere, stays
    /// exactly as they left it, debugging or not.
    /// </summary>
    public void ExitDebugLayout()
    {
        foreach (var tool in DebugLayoutTools)
        {
            if (_openedForDebugLayout.Remove(tool) && tool.Owner is not null)
                RemoveDockable(tool, collapse: false);
        }
    }

    public override void InitLayout(IDockable layout)
    {
        // Dock maps each dockable type to its own view; here the contents are
        // controls built in code, so the mapping happens in the view through a
        // custom DataTemplate.
        DockableLocator = new Dictionary<string, Func<IDockable?>>();
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new Dock.Avalonia.Controls.HostWindow()
        };

        base.InitLayout(layout);
    }
}
