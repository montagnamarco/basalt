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

    public SolutionExplorerTool SolutionExplorer { get; } = new();
    public ToolboxTool Toolbox { get; } = new();
    public PropertiesTool Properties { get; } = new();
    public ProblemsTool Problems { get; } = new();
    public OutputTool Output { get; } = new();
    public SearchTool Search { get; } = new();
    public OutlineTool Outline { get; } = new();
    public ElementTreeTool ElementTree { get; } = new();
    public DatabaseTool Database { get; } = new();
    public EventsTool Events { get; } = new();
    public ReferencesTool References { get; } = new();
    public CallStackTool CallStack { get; } = new();
    public VariablesTool Variables { get; } = new();
    public BreakpointsTool Breakpoints { get; } = new();
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
            Proportion = 0.11,
            VisibleDockables = CreateList<IDockable>(Toolbox, ElementTree, Outline),
            ActiveDockable = Toolbox
        };

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
        var propertiesPanel = new ToolDock
        {
            Id = "DestraSotto",
            Title = "DestraSotto",
            Alignment = Alignment.Right,
            Proportion = 0.65,
            VisibleDockables = CreateList<IDockable>(Properties, Events, Assistant),
            ActiveDockable = Properties
        };

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
            // Ordered by how often they are wanted, and grouped by what they
            // are about: what is wrong, what was printed, what was searched
            // for, then the debugging three together, then the rest. A strip
            // of a dozen tabs in the order they happened to be written is one
            // nobody learns the shape of.
            VisibleDockables = CreateList<IDockable>(
                Problems, Output, Search, References,
                CallStack, Variables, Breakpoints,
                Tests, Database, GitHistory, GitDiff, terminal),
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
