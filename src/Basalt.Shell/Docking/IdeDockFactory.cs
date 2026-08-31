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

        var pannelloSinistro = new ToolDock
        {
            Id = "Sinistra",
            Title = "Sinistra",
            Alignment = Alignment.Left,
            Proportion = 0.2,
            VisibleDockables = CreateList<IDockable>(SolutionExplorer, GitChanges, GitBranches),
            ActiveDockable = SolutionExplorer
        };

        // The right-hand side is two panels stacked, not one with four tabs:
        // the toolbox and the properties are used together — drop a control,
        // then set what you just dropped — and as tabs each hides the other
        // at the moment it is wanted.
        var destraSopra = new ToolDock
        {
            Id = "DestraSopra",
            Title = "DestraSopra",
            Alignment = Alignment.Right,
            Proportion = 0.45,
            VisibleDockables = CreateList<IDockable>(Toolbox, ElementTree, Outline),
            ActiveDockable = Toolbox
        };

        var destraSotto = new ToolDock
        {
            Id = "DestraSotto",
            Title = "DestraSotto",
            Alignment = Alignment.Right,
            Proportion = 0.55,
            VisibleDockables = CreateList<IDockable>(Properties, Events, Assistant),
            ActiveDockable = Properties
        };

        var pannelloDestro = new ProportionalDock
        {
            Id = "Destra",
            Title = "Destra",
            Orientation = Orientation.Vertical,
            Proportion = 0.22,
            VisibleDockables = CreateList<IDockable>(
                destraSopra,
                new ProportionalDockSplitter(),
                destraSotto)
        };

        var terminal = CreateTerminal();

        BottomArea = new ToolDock
        {
            Id = "Inferiore",
            Title = "Inferiore",
            Alignment = Alignment.Bottom,
            Proportion = 0.28,
            VisibleDockables = CreateList<IDockable>(
                Problems, Output, Search, References,
                CallStack, Variables, Breakpoints, Tests, Database, GitHistory, GitDiff, terminal),
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
                pannelloSinistro,
                new ProportionalDockSplitter(),
                colonnaCentrale,
                new ProportionalDockSplitter(),
                pannelloDestro)
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
