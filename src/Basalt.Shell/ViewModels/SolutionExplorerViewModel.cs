using System.Collections.ObjectModel;
using System.Globalization;
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

    private static readonly Lazy<HashSet<string>> ResourceCultures = new(() =>
        CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Select(culture => culture.Name)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

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

    /// <summary>
    /// True while <see cref="SelectedNode"/> is being set by the tree itself
    /// — Refresh, Show All Files, Reveal — rather than by the user clicking a
    /// row. Selecting a form is otherwise the same action as opening its
    /// designer, and a selection the tree restores on its own must not open
    /// anything.
    /// </summary>
    public bool IsProgrammaticSelection { get; private set; }

    /// <summary>Selects a path without it counting as the user's own choice.</summary>
    public bool SelectPathQuietly(string path)
    {
        IsProgrammaticSelection = true;
        try { return SelectPath(path); }
        finally { IsProgrammaticSelection = false; }
    }

    /// <summary>Reads the tree again from the same place.</summary>
    public void Refresh()
    {
        if (LoadedPath is not { Length: > 0 } path) return;

        var expanded = EnumerateNodes().Where(node => node.IsExpanded)
            .Select(node => node.Path).ToHashSet(StringComparer.Ordinal);
        var selected = SelectedNode?.Path;
        Load(path);

        foreach (var node in EnumerateNodes())
            if (expanded.Contains(node.Path)) node.IsExpanded = true;

        if (selected is not null) SelectPathQuietly(selected);
    }

    partial void OnShowAllFilesChanged(bool value) => Refresh();

    public void Load(string solutionOrProjectPath)
    {
        LoadedPath = solutionOrProjectPath;

        SelectedNode = null;
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

    public bool SelectPath(string path)
    {
        foreach (var root in Roots)
        {
            if (!SelectPath(root, path, [])) continue;
            return true;
        }

        return false;
    }

    private bool SelectPath(SolutionTreeNode node, string path, List<SolutionTreeNode> ancestors)
    {
        if (string.Equals(node.Path, path, StringComparison.Ordinal))
        {
            if (SelectedNode is not null) SelectedNode.IsSelected = false;
            SelectedNode = node;
            node.IsSelected = true;
            foreach (var ancestor in ancestors) ancestor.IsExpanded = true;
            return true;
        }

        ancestors.Add(node);
        foreach (var child in node.Children)
            if (SelectPath(child, path, ancestors)) return true;
        ancestors.RemoveAt(ancestors.Count - 1);
        return false;
    }

    private IEnumerable<SolutionTreeNode> EnumerateNodes()
    {
        foreach (var root in Roots)
            foreach (var node in EnumerateNodes(root))
                yield return node;
    }

    private static IEnumerable<SolutionTreeNode> EnumerateNodes(SolutionTreeNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in EnumerateNodes(child))
                yield return descendant;
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

            // Shown even with nothing in it yet: a folder the user just
            // created from "New Folder…" has no files the moment it exists,
            // and hiding it the instant Explorer.Refresh() ran left nowhere
            // obvious to add anything into — Visual Studio keeps an empty
            // folder visible too, exactly so it can receive what is dropped
            // or created into it next.
            parent.Children.Add(node);
        }

        var files = Directory.EnumerateFiles(directory)
            .Where(file => !IsNoise(Path.GetFileName(file)))
            .Where(file => rootFile is null || Path.GetFullPath(file) != rootFile)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var visible = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) visible.TryAdd(file, file);
        var companions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase))
                continue;

            var stem = file[..^3];
            var designer = stem + ".Designer.vb";
            if (visible.TryGetValue(designer, out var designerFile))
                companions.Add(designerFile);
            foreach (var resourceFile in ResourceCompanions(files, stem))
                companions.Add(resourceFile);
        }

        foreach (var file in files)
        {
            if (companions.Contains(file)) continue;

            var node = new SolutionTreeNode(Path.GetFileName(file), file, Classify(file));
            var stem = file.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)
                ? file[..^3]
                : null;
            if (stem is not null)
            {
                var designer = stem + ".Designer.vb";
                if (visible.TryGetValue(designer, out var designerFile))
                {
                    node.DesignerPath = designerFile;
                    node.Children.Add(new SolutionTreeNode(Path.GetFileName(designerFile), designerFile, NodeKind.SourceFile));
                }

                foreach (var resourceFile in ResourceCompanions(files, stem))
                    node.Children.Add(new SolutionTreeNode(Path.GetFileName(resourceFile), resourceFile, NodeKind.Other));
            }

            parent.Children.Add(node);
        }
    }

    private static IEnumerable<string> ResourceCompanions(IEnumerable<string> files, string formStem) =>
        files.Where(file => IsResourceCompanion(file, formStem))
            .OrderBy(file => file.Equals(formStem + ".resx", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file.Length)
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase);

    private static bool IsResourceCompanion(string path, string formStem)
    {
        if (!path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)
            || !path.StartsWith(formStem, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = path[formStem.Length..^".resx".Length];
        if (suffix.Length == 0) return true;
        if (suffix[0] != '.' || suffix.Length == 1) return false;

        return ResourceCultures.Value.Contains(suffix[1..]);
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
