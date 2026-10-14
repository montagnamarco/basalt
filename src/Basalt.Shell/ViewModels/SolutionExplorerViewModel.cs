using System.Collections.ObjectModel;
using System.Globalization;
using Basalt.Core.Features;
using Basalt.Workspace;
using Basalt.Workspace.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Basalt.Shell.ViewModels;

/// <summary>File tree of the open solution.</summary>
public sealed partial class SolutionExplorerViewModel : ObservableObject
{
    private readonly IdeFeatures _features;

    public SolutionExplorerViewModel() : this(IdeFeatures.Default) { }

    /// <summary>
    /// The tree the window's features shape: with the legacy switch off a
    /// .frm is an ordinary file, with no designer and no "Open in Designer".
    /// </summary>
    public SolutionExplorerViewModel(IdeFeatures features) => _features = features;

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

    /// <summary>
    /// How paths compare here: ignoring case on Windows and macOS, whose
    /// default file systems do, exactly on Linux. The solution's project paths
    /// and the folders walked are compared with it.
    /// </summary>
    private static StringComparison PathCase { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer PathComparer { get; } =
        PathCase == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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

        // Exactly as they were, not only the open ones opened again: Load
        // opens the root and the projects by default, and a folder the user
        // had closed must stay closed rather than come back open.
        foreach (var node in EnumerateNodes())
            node.IsExpanded = expanded.Contains(node.Path);

        if (selected is not null) SelectPathQuietly(selected);
    }

    partial void OnShowAllFilesChanged(bool value) => Refresh();

    /// <summary>The project file each folder belongs to, read once per load.</summary>
    private readonly Dictionary<string, string?> _projectOfDirectory = new(StringComparer.Ordinal);

    /// <summary>The text of each project file, read once per load; null when it cannot be read.</summary>
    private readonly Dictionary<string, string?> _projectTexts = new(StringComparer.Ordinal);

    /// <summary>
    /// The projects the loaded solution lists, in its order, or null when the
    /// tree is a project's and not a solution's. A solution's project is the one
    /// its file names, wherever that is on disk; a project file in a folder the
    /// solution does not list is a file, not a project of the solution.
    /// </summary>
    private List<string>? _solutionProjects;

    /// <summary>The same projects as a set, for the membership tests made per file.</summary>
    private HashSet<string>? _solutionProjectSet;

    /// <summary>The folder the loaded solution file sits in.</summary>
    private string? _solutionDirectory;

    /// <summary>The solution projects already shown as a node, during one load.</summary>
    private readonly HashSet<string> _shownProjects = new(PathComparer);

    /// <summary>
    /// Whether the project a file belongs to includes it. A file in no
    /// project, or in a project that is not SDK-style, is included.
    /// </summary>
    private ProjectItemEditor.Membership MembershipOf(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (directory is null) return ProjectItemEditor.Membership.Included;

        if (!_projectOfDirectory.TryGetValue(directory, out var project))
            _projectOfDirectory[directory] = project = ExplorerFileOperations.ProjectContaining(filePath);
        if (project is null) return ProjectItemEditor.Membership.Included;

        if (!_projectTexts.TryGetValue(project, out var text))
        {
            try { text = File.ReadAllText(project); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { text = null; }
            _projectTexts[project] = text;
        }

        return text is null
            ? ProjectItemEditor.Membership.Included
            : ProjectItemEditor.MembershipOf(project, text, filePath);
    }

    public void Load(string solutionOrProjectPath)
    {
        LoadedPath = solutionOrProjectPath;

        SelectedNode = null;
        Roots.Clear();

        // Read again from the disk on every load, so a project edited since
        // the last one, by Exclude or by another editor, is seen as it is now.
        _projectOfDirectory.Clear();
        _projectTexts.Clear();
        _shownProjects.Clear();

        var isSolution = SolutionFile.IsSolutionFile(solutionOrProjectPath);
        // A solution file that lists no projects keeps the folder layout it always had.
        var listed = isSolution ? SolutionFile.ProjectsOf(solutionOrProjectPath) : [];
        _solutionProjects = listed.Count > 0 ? listed.ToList() : null;
        _solutionProjectSet = _solutionProjects is null ? null : new HashSet<string>(_solutionProjects, PathComparer);

        var root = new SolutionTreeNode(
            Path.GetFileName(solutionOrProjectPath),
            solutionOrProjectPath,
            isSolution ? NodeKind.Solution : NodeKind.Project)
        { IsExpanded = true };

        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionOrProjectPath));
        _solutionDirectory = directory;
        if (directory is not null) PopulateFrom(root, directory, inSolution: isSolution);

        if (isSolution) AddSolutionProjectsOutsideTree(root);

        // Visual Studio opens a solution with its projects showing, one level
        // deep: each project's own folders stay closed. A remembered session
        // restores its exact expansion afterwards, so this is only the default.
        if (isSolution)
            foreach (var project in root.Children.Where(child => child.Kind == NodeKind.Project))
                project.IsExpanded = true;

        Roots.Add(root);
        Loaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows each solution project that no folder of the tree already stands
    /// for: one the solution keeps outside its own folder, such as ..\App\App.vbproj.
    /// Each one is a project node with its own folder beneath it, the way a
    /// project in a folder of the solution is.
    /// </summary>
    private void AddSolutionProjectsOutsideTree(SolutionTreeNode root)
    {
        foreach (var project in _solutionProjects ?? [])
        {
            if (_shownProjects.Contains(project)) continue;

            // A project in the solution's own folder is a file of the root, as
            // it always was: the root lists that folder's files already.
            var folder = Path.GetDirectoryName(project)!;
            if (string.Equals(folder, _solutionDirectory, PathCase)) continue;

            _shownProjects.Add(project);
            var node = new SolutionTreeNode(Path.GetFileNameWithoutExtension(project), project, NodeKind.Project)
            {
                FolderPath = folder
            };
            PopulateFrom(node, folder, inSolution: true);
            root.Children.Add(node);
        }
    }

    /// <summary>Raised after the tree has been read from disk, so the panel can watch the same folder.</summary>
    public event EventHandler? Loaded;

    /// <summary>
    /// Whether a path under the loaded folder is one the tree lists: not
    /// inside an output or tooling folder, and not a tooling file, unless
    /// Show All Files is on. A change to anything else cannot change the
    /// tree, so the disk is not read again for it, which matters because a
    /// build writes hundreds of files under obj.
    /// </summary>
    internal bool IsListed(string path)
    {
        if (ShowAllFiles) return true;
        if (LoadedPath is not { Length: > 0 } loaded) return false;

        var directory = Path.GetDirectoryName(Path.GetFullPath(loaded));
        if (directory is null) return false;

        var parts = Path.GetRelativePath(directory, Path.GetFullPath(path))
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        for (var i = 0; i < parts.Length - 1; i++)
            if (Ignored.Contains(parts[i])) return false;

        return !IsNoise(parts[^1]);
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
        // A project under a solution is also named by its folder, so a
        // selection saved before it became a project node still finds it.
        if (string.Equals(node.Path, path, StringComparison.Ordinal)
            || string.Equals(node.FolderPath, path, StringComparison.Ordinal))
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

    /// <summary>
    /// Lists a folder's subfolders and files under <paramref name="parent"/>.
    /// </summary>
    /// <param name="inSolution">
    /// Whether the tree is a solution's. Only then does a folder holding one
    /// project file stand for that project; a project opened alone keeps its
    /// folders as they were.
    /// </param>
    private void PopulateFrom(SolutionTreeNode parent, string directory, bool inSolution)
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

            // The folder of one project is that project, as Visual Studio shows
            // it: its node is the project file, and the file is then not listed
            // again inside it, as for the root. A folder with none, or with two
            // or more, stays a folder. Only a project the solution lists counts.
            var projectFile = inSolution ? SoleProjectFileIn(subdirectory) : null;
            if (projectFile is not null) _shownProjects.Add(projectFile);
            var node = projectFile is null
                ? new SolutionTreeNode(name, subdirectory, NodeKind.Folder)
                : new SolutionTreeNode(Path.GetFileNameWithoutExtension(projectFile), projectFile, NodeKind.Project)
                {
                    FolderPath = subdirectory
                };
            PopulateFrom(node, subdirectory, inSolution);

            // Shown even with nothing in it yet: a folder the user just
            // created from "New Folder…" has no files the moment it exists,
            // and hiding it the instant Explorer.Refresh() ran left nowhere
            // obvious to add anything into — Visual Studio keeps an empty
            // folder visible too, exactly so it can receive what is dropped
            // or created into it next.
            parent.Children.Add(node);
        }

        var listed = Directory.EnumerateFiles(directory)
            .Where(file => !IsNoise(Path.GetFileName(file)))
            .Where(file => rootFile is null || Path.GetFullPath(file) != rootFile)
            .Where(file => !IsSolutionProjectShownElsewhere(file, directory, inSolution))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // A file the project leaves out is hidden, as the tooling files are,
        // until Show All Files asks for it; then it is greyed out in the tree.
        var outOfProject = new HashSet<string>(
            listed.Where(file => MembershipOf(file) != ProjectItemEditor.Membership.Included),
            StringComparer.OrdinalIgnoreCase);

        var files = ShowAllFiles
            ? listed
            : listed.Where(file => !outOfProject.Contains(file)).ToArray();

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

            var node = new SolutionTreeNode(Path.GetFileName(file), file, Classify(file))
            {
                IsOutOfProject = outOfProject.Contains(file)
            };
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

    /// <summary>
    /// Whether a project file of the solution is listed as its own node rather
    /// than as a file here. The solution's own folder keeps its project files as
    /// files, as before; a project file in any other folder is a node, whether
    /// its folder is shown as one or not.
    /// </summary>
    private bool IsSolutionProjectShownElsewhere(string file, string directory, bool inSolution) =>
        inSolution
        && _solutionProjects is not null
        && !string.Equals(directory, _solutionDirectory, PathCase)
        && _solutionProjectSet!.Contains(file);

    /// <summary>
    /// The project file a folder holds when it holds exactly one, or null when
    /// it holds none or several: a folder with two projects has no single
    /// project to stand for it. With a solution's list, only the projects it
    /// lists count; without one, any project file does.
    /// </summary>
    private string? SoleProjectFileIn(string directory)
    {
        string? found = null;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (Path.GetExtension(file).ToLowerInvariant() is not (".vbproj" or ".csproj")) continue;
            if (_solutionProjectSet is not null && !_solutionProjectSet.Contains(file)) continue;
            if (found is not null) return null;
            found = file;
        }

        return found;
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
    private NodeKind Classify(string path)
    {
        // A project file the loaded solution does not list is a file of the
        // folder, not a project of the solution.
        if (_solutionProjectSet is not null
            && SolutionFile.IsProjectFile(path)
            && !_solutionProjectSet.Contains(path))
            return NodeKind.Other;

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".axaml" or ".xaml" => NodeKind.XamlFile,

            // A .frm opens in the designer too: it is read and turned into
            // the markup the designer works on, and the file itself is never
            // rewritten. Only with the legacy switch on; otherwise it is text.
            ".frm" when _features.LegacyBasic => NodeKind.XamlFile,
            ".cshtml" or ".vbhtml" or ".razor" => NodeKind.RazorFile,
            ".cs" or ".vb" => NodeKind.SourceFile,
            ".csproj" or ".vbproj" or ".sln" or ".slnx" => NodeKind.Project,
            ".json" or ".config" or ".xml" or ".editorconfig"
                or ".props" or ".targets" or ".yml" or ".yaml" => NodeKind.ConfigFile,
            ".css" or ".js" or ".html" or ".htm" or ".scss" or ".map" => NodeKind.WebAsset,
            _ => NodeKind.Other
        };
    }
}
