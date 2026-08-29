using Basalt.Shell.Controls;
using Dock.Model.Mvvm.Controls;
using Basalt.Core.Localization;

namespace Basalt.Shell.Docking;

/// <summary>
/// The IDE's dockable panels.
///
/// Every panel is a Dock Tool: it can be moved, docked side by side, floated
/// into a separate window or closed, and the resulting arrangement can be
/// saved. The actual content is built by the view, which maps each type to the
/// corresponding control.
/// </summary>
public abstract class IdeTool : Tool
{
    /// <summary>
    /// The icon shown in the panel's header.
    ///
    /// Dock has no icon of its own — its Tool carries a title and nothing
    /// else — so the panel draws its own header rather than the title bar
    /// drawing it.
    /// </summary>
    public IconKind Icon { get; protected init; } = IconKind.None;
}

public sealed class SolutionExplorerTool : IdeTool
{
    public SolutionExplorerTool()
    {
        Id = "SolutionExplorer";
        Title = Localizer.Get(StringKeys.ToolSolutionExplorer);
        Icon = IconKind.Explorer;
    }
}

public sealed class ToolboxTool : IdeTool
{
    public ToolboxTool()
    {
        Id = "Toolbox";
        Title = Localizer.Get(StringKeys.ToolToolbox);
        Icon = IconKind.Toolbox;
    }
}

public sealed class PropertiesTool : IdeTool
{
    public PropertiesTool()
    {
        Id = "Properties";
        Title = Localizer.Get(StringKeys.ToolProperties);
        Icon = IconKind.Properties;
    }
}

public sealed class ProblemsTool : IdeTool
{
    public ProblemsTool()
    {
        Id = "Problems";
        Title = Localizer.Get(StringKeys.ToolProblems);
        Icon = IconKind.Problems;
    }
}

public sealed class OutputTool : IdeTool
{
    public OutputTool()
    {
        Id = "Output";
        Title = Localizer.Get(StringKeys.ToolOutput);
        Icon = IconKind.Output;
    }
}

/// <summary>Results of searching across the solution.</summary>
public sealed class SearchTool : IdeTool
{
    public SearchTool()
    {
        Id = "Search";
        Title = Localizer.Get(StringKeys.ToolSearch);
        Icon = IconKind.Find;
    }
}

/// <summary>Declarations in the open document.</summary>
public sealed class OutlineTool : IdeTool
{
    public OutlineTool()
    {
        Id = "Outline";
        Title = Localizer.Get(StringKeys.ToolOutline);
        Icon = IconKind.Outline;
    }
}

/// <summary>The controls of the form being designed.</summary>
public sealed class ElementTreeTool : IdeTool
{
    public ElementTreeTool()
    {
        Id = "ElementTree";
        Title = Localizer.Get(StringKeys.ToolElementTree);
        Icon = IconKind.Outline;
    }
}

/// <summary>A database, its tables, and a place to query it.</summary>
public sealed class DatabaseTool : IdeTool
{
    public DatabaseTool()
    {
        Id = "Database";
        Title = Localizer.Get(StringKeys.ToolDatabase);
        Icon = IconKind.JsonFile;
    }
}

/// <summary>Everywhere a symbol is used.</summary>
public sealed class ReferencesTool : IdeTool
{
    public ReferencesTool()
    {
        Id = "References";
        Title = Localizer.Get(StringKeys.ToolReferences);
        Icon = IconKind.References;
    }
}

/// <summary>Where execution is stopped, innermost call first.</summary>
public sealed class CallStackTool : IdeTool
{
    public CallStackTool()
    {
        Id = "CallStack";
        Title = Localizer.Get(StringKeys.ToolCallStack);
        Icon = IconKind.Debug;
    }
}

/// <summary>The variables of the frame being looked at.</summary>
public sealed class VariablesTool : IdeTool
{
    public VariablesTool()
    {
        Id = "Variables";
        Title = Localizer.Get(StringKeys.ToolVariables);
        Icon = IconKind.Information;
    }
}

/// <summary>Every breakpoint in the solution.</summary>
public sealed class BreakpointsTool : IdeTool
{
    public BreakpointsTool()
    {
        Id = "Breakpoints";
        Title = Localizer.Get(StringKeys.ToolBreakpoints);
        Icon = IconKind.Breakpoint;
    }
}

/// <summary>The tests of the solution, and how they last ran.</summary>
public sealed class TestsTool : IdeTool
{
    public TestsTool()
    {
        Id = "Tests";
        Title = Localizer.Get(StringKeys.ToolTests);
        Icon = IconKind.Tests;
    }
}

/// <summary>A conversation with an assistant, beside the code.</summary>
public sealed class AssistantTool : IdeTool
{
    public AssistantTool()
    {
        Id = "Assistant";
        Title = Localizer.Get(StringKeys.ToolAssistant);
        Icon = IconKind.Assistant;
    }
}

/// <summary>Uncommitted changes, staged and not.</summary>
public sealed class GitChangesTool : IdeTool
{
    public GitChangesTool()
    {
        Id = "GitChanges";
        Title = Localizer.Get(StringKeys.ToolGitChanges);
        Icon = IconKind.Commit;
    }
}

/// <summary>The branches of the repository.</summary>
public sealed class GitBranchesTool : IdeTool
{
    public GitBranchesTool()
    {
        Id = "GitBranches";
        Title = Localizer.Get(StringKeys.ToolGitBranches);
        Icon = IconKind.Branch;
    }
}

/// <summary>The changes in the selected file.</summary>
public sealed class GitDiffTool : IdeTool
{
    public GitDiffTool()
    {
        Id = "GitDiff";
        Title = Localizer.Get(StringKeys.ToolGitDiff);
        Icon = IconKind.Diff;
    }
}

/// <summary>The commit history, drawn as a graph.</summary>
public sealed class GitHistoryTool : IdeTool
{
    public GitHistoryTool()
    {
        Id = "GitHistory";
        Title = Localizer.Get(StringKeys.ToolGitHistory);
        Icon = IconKind.History;
    }
}

/// <summary>
/// A terminal. Unlike the other panels, several can be opened at once, each
/// with its own shell session.
/// </summary>
public sealed class TerminalTool : IdeTool
{
    public TerminalTool(string workingDirectory, int number)
    {
        WorkingDirectory = workingDirectory;
        Id = $"Terminal{number}";
        Title = number == 1
            ? Localizer.Get(StringKeys.ToolTerminal)
            : Localizer.Get(StringKeys.ToolTerminalNumbered, number);
        Icon = IconKind.Terminal;
    }

    public string WorkingDirectory { get; }
}

/// <summary>An open document: a code file in the editor or a window in the designer.</summary>
public sealed class IdeDocument : Document
{
    public IdeDocument(ViewModels.EditorDocumentViewModel document)
    {
        Document = document;
        Id = $"{document.FilePath}|{document.OpenInDesigner}";
        Title = document.Title;
    }

    public ViewModels.EditorDocumentViewModel Document { get; }
}
