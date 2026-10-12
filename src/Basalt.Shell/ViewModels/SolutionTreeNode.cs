using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Basalt.Core.Model;

namespace Basalt.Shell.ViewModels;

/// <summary>What a node in the solution tree stands for.</summary>
public enum NodeKind
{
    Solution,
    Project,
    Folder,

    /// <summary>A Visual Basic source file.</summary>
    SourceFile,

    /// <summary>An .axaml or .xaml file, editable in the visual designer.</summary>
    XamlFile,

    /// <summary>A Razor view: .cshtml for C#, .vbhtml for Visual Basic.</summary>
    RazorFile,

    /// <summary>Configuration such as appsettings.json or launchSettings.json.</summary>
    ConfigFile,

    /// <summary>Static web content: stylesheets, scripts, markup.</summary>
    WebAsset,

    /// <summary>Any other file the project carries.</summary>
    Other
}

/// <summary>A node in the solution tree.</summary>
public sealed partial class SolutionTreeNode : ObservableObject
{
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public SolutionTreeNode(string name, string path, NodeKind kind)
    {
        Name = name;
        Path = path;
        Kind = kind;
    }

    public string Name { get; }
    public string Path { get; }
    public NodeKind Kind { get; }
    public ObservableCollection<SolutionTreeNode> Children { get; } = [];

    /// <summary>The generated companion opened when this form enters the designer.</summary>
    public string? DesignerPath { get; internal set; }

    public bool IsForm => DesignerPath is not null;

    /// <summary>
    /// The project leaves this file out (Exclude From Project, or a file the
    /// default globs do not reach). Shown only under Show All Files, greyed.
    /// </summary>
    public bool IsOutOfProject { get; internal set; }

    public SourceLanguage Language => SourceLanguageExtensions.FromPath(Path);

    /// <summary>An .axaml file can be opened in the visual designer.</summary>
    public bool IsDesignable => DesignerPath is not null || Kind == NodeKind.XamlFile
        || (Kind == NodeKind.SourceFile && Path.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether double-clicking the node opens it in an editor.</summary>
    public bool IsOpenable => Kind is NodeKind.SourceFile
                                   or NodeKind.XamlFile
                                   or NodeKind.RazorFile
                                   or NodeKind.ConfigFile
                                   or NodeKind.WebAsset
                                   or NodeKind.Other;

    /// <summary>
    /// Whether Rename and Delete apply to this node.
    ///
    /// False for the solution and for a project: their own path is the
    /// .sln/.slnx or .csproj/.vbproj the tree was loaded from
    /// (<see cref="SolutionExplorerViewModel.LoadedPath"/>). Renaming or
    /// deleting that file out from under the open session leaves
    /// <c>ReloadAsync</c> reopening a path that no longer exists.
    /// </summary>
    public bool CanRenameOrDelete => Kind is not (NodeKind.Solution or NodeKind.Project);

    public string Icon => Kind switch
    {
        NodeKind.Solution => "\U0001F4E6",
        NodeKind.Project => "\U0001F4C1",
        NodeKind.Folder => "\U0001F4C2",
        NodeKind.XamlFile => "\U0001F3A8",
        NodeKind.RazorFile => "\U0001F310",
        NodeKind.ConfigFile => "\u2699\uFE0F",
        NodeKind.WebAsset => "\U0001F3AF",
        NodeKind.SourceFile => Language == SourceLanguage.VisualBasic ? "VB" : "C#",
        _ => "\U0001F4C4"
    };
}
