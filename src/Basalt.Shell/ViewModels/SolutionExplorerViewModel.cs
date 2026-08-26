using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Basalt.Shell.ViewModels;

/// <summary>File tree of the open solution.</summary>
public sealed partial class SolutionExplorerViewModel : ObservableObject
{
    /// <summary>Output folders the user does not care about.</summary>
    /// <summary>
    /// Files that belong to the tooling rather than to the project, and that
    /// would only add noise to the tree.
    /// </summary>
    private bool IsNoise(string fileName) =>
        !ShowAllFiles
        && (fileName.StartsWith('.') && fileName is not ".editorconfig"
            || fileName.EndsWith(".user", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".cache", StringComparison.OrdinalIgnoreCase));

    private static readonly HashSet<string> Ignored =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

    [ObservableProperty] private SolutionTreeNode? _selectedNode;

    /// <summary>
    /// Whether the output folders and the tooling's own files are listed too.
    ///
    /// Off by default: bin, obj and .git are noise nearly all the time. On
    /// when something has to be found that the tree is hiding — a stale build
    /// output, a file in .vs — which otherwise means leaving the IDE.
    /// </summary>
    [ObservableProperty] private bool _showAllFiles;

    public ObservableCollection<SolutionTreeNode> Roots { get; } = [];

    /// <summary>What was last loaded, so it can be loaded again.</summary>
    public string? LoadedPath { get; private set; }

    /// <summary>Reads the tree again from the same place.</summary>
    public void Refresh()
    {
        if (LoadedPath is { Length: > 0 } path) Load(path);
    }

    partial void OnShowAllFilesChanged(bool value) => Refresh();

    public void Load(string solutionOrProjectPath)
    {
        LoadedPath = solutionOrProjectPath;

        Roots.Clear();

        var isSolution = Path.GetExtension(solutionOrProjectPath)
            is ".sln" or ".slnx";

        var root = new SolutionTreeNode(
            Path.GetFileName(solutionOrProjectPath),
            solutionOrProjectPath,
            isSolution ? NodeKind.Solution : NodeKind.Project)
        { IsExpanded = true };

        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionOrProjectPath));
        if (directory is not null) PopulateFrom(root, directory);

        Roots.Add(root);
    }

    private void PopulateFrom(SolutionTreeNode parent, string directory)
    {
        // The root already stands for the solution or project file itself, so
        // listing it again among its own children would duplicate it.
        var rootFile = parent.Kind is NodeKind.Solution or NodeKind.Project
            ? Path.GetFullPath(parent.Path)
            : null;

        foreach (var subdirectory in Directory.EnumerateDirectories(directory).OrderBy(d => d))
        {
            var name = Path.GetFileName(subdirectory);
            if (!ShowAllFiles && Ignored.Contains(name)) continue;

            var node = new SolutionTreeNode(name, subdirectory, NodeKind.Folder);
            PopulateFrom(node, subdirectory);

            // A folder with no interesting files adds nothing to the tree.
            if (node.Children.Count > 0) parent.Children.Add(node);
        }

        foreach (var file in Directory.EnumerateFiles(directory).OrderBy(f => f))
        {
            var name = Path.GetFileName(file);
            if (IsNoise(name)) continue;
            if (rootFile is not null && Path.GetFullPath(file) == rootFile) continue;

            parent.Children.Add(new SolutionTreeNode(name, file, Classify(file)));
        }
    }

    /// <summary>
    /// What kind of node a file becomes in the tree.
    ///
    /// Unrecognised files are shown rather than hidden: a solution explorer
    /// that silently drops what it does not know leaves the project looking
    /// half empty, and the user with no way to tell whether the file is there.
    /// </summary>
    private static NodeKind Classify(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            // A .frm opens in the designer too: it is read and turned into
            // the markup the designer works on, and the file itself is never
            // rewritten.
            ".axaml" or ".xaml" or ".frm" => NodeKind.XamlFile,
            ".cshtml" or ".vbhtml" or ".razor" => NodeKind.RazorFile,
            ".cs" or ".vb" => NodeKind.SourceFile,
            ".csproj" or ".vbproj" or ".sln" or ".slnx" => NodeKind.Project,
            ".json" or ".config" or ".xml" or ".editorconfig"
                or ".props" or ".targets" or ".yml" or ".yaml" => NodeKind.ConfigFile,
            ".css" or ".js" or ".html" or ".htm" or ".scss" or ".map" => NodeKind.WebAsset,
            _ => NodeKind.Other
        };
}
