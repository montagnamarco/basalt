using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Shell;

/// <summary>
/// Project &gt; References… — VB6's References/Components, Visual Studio's
/// Reference Manager — for the three kinds of reference a Basalt project
/// file can carry: other projects of the solution, assemblies by path, and
/// NuGet packages.
///
/// Every check, add and remove edits an in-memory copy of the project file's
/// own text through <see cref="ProjectReferenceEditor"/> rather than a model
/// rebuilt from scratch, so OK only ever has to write (or hand to an open
/// editor) the text that copy has become; Cancel simply never looks at it
/// again.
/// </summary>
public sealed class ReferencesDialog : Window
{
    private readonly string _projectPath;
    private readonly Action<string> _applyXml;
    private string _workingXml;
    private readonly List<(string RelativePath, string DisplayName, CheckBox Box)> _projectRows = [];
    private readonly ListBox _assemblies;
    private readonly ListBox _packages;
    private readonly TextBox _packageId;
    private readonly TextBox _packageVersion;
    private readonly TextBlock _packageMessage;
    private readonly bool _centralPackageManagement;

    /// <summary>
    /// The project file's text as OK left it, for a caller that wants to
    /// know without re-reading disk. Null until OK is clicked.
    /// </summary>
    public string? AcceptedXml { get; private set; }

    /// <summary>
    /// Opens the project file on disk. OK writes the result straight back
    /// to it. A project file already open in an editor tab needs
    /// <paramref name="applyXml"/> instead, so the edit lands as one undoable
    /// change to that tab's own buffer rather than a write a stale buffer
    /// would overwrite the moment it is next saved.
    /// </summary>
    public ReferencesDialog(
        string projectPath, IReadOnlyList<string> otherProjectPaths, Action<string>? applyXml = null)
        : this(projectPath, otherProjectPaths, File.ReadAllText(projectPath), applyXml)
    {
    }

    /// <summary>For tests: the project's text handed in directly, so no file has to exist on disk.</summary>
    internal ReferencesDialog(
        string projectPath, IReadOnlyList<string> otherProjectPaths, string projectXml, Action<string>? applyXml = null)
    {
        _projectPath = projectPath;
        _workingXml = projectXml;
        _applyXml = applyXml ?? CreateDiskWriter(projectPath);
        _centralPackageManagement = DetectsCentralPackageManagement(projectPath, projectXml);

        Title = Localizer.Get(StringKeys.ReferencesTitle, Path.GetFileNameWithoutExtension(projectPath));
        Width = 520;
        Height = 420;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var projectsPage = BuildProjectsPage(projectPath, otherProjectPaths);

        _assemblies = new ListBox { Height = 180 };
        var assembliesPage = BuildAssembliesPage();

        _packages = new ListBox { Height = 150 };
        _packageId = new TextBox { PlaceholderText = Localizer.Get(StringKeys.ReferencesPackageId) };
        _packageVersion = new TextBox
        {
            PlaceholderText = Localizer.Get(StringKeys.ReferencesPackageVersion),
            Margin = new Thickness(8, 0, 8, 0)
        };
        _packageMessage = new TextBlock
        {
            Text = Localizer.Get(StringKeys.ReferencesPackageManagedCentrally),
            IsVisible = _centralPackageManagement,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Opacity = 0.8
        };
        var packagesPage = BuildPackagesPage();

        RefreshAssemblies();
        RefreshPackages();

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = Localizer.Get(StringKeys.ReferencesTabProjects), Content = projectsPage },
                new TabItem { Header = Localizer.Get(StringKeys.ReferencesTabAssemblies), Content = assembliesPage },
                new TabItem { Header = Localizer.Get(StringKeys.ReferencesTabPackages), Content = packagesPage }
            }
        };

        OkButton = new Button { Content = Localizer.Get(StringKeys.ButtonOk), IsDefault = true };
        CancelButton = new Button { Content = Localizer.Get(StringKeys.ButtonCancel), IsCancel = true };
        OkButton.Click += (_, _) => Accept();
        CancelButton.Click += (_, _) => Close(false);

        Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new Border
                {
                    [DockPanel.DockProperty] = Avalonia.Controls.Dock.Bottom,
                    Margin = new Thickness(0, 12, 0, 0),
                    Child = DialogButtons.Row(OkButton, CancelButton)
                },
                tabs
            }
        };
    }

    /// <summary>For tests: the dialog's own OK button, the Windows order's primary action.</summary>
    internal Button OkButton { get; }

    /// <summary>For tests: the dialog's own Cancel button.</summary>
    internal Button CancelButton { get; }

    // Projects

    private Control BuildProjectsPage(string projectPath, IReadOnlyList<string> otherProjectPaths)
    {
        // Case-insensitively, the way MSBuild itself matches a path: a
        // reference the file already has in a different case must still
        // start the box checked rather than invite a second, conflicting
        // item for what MSBuild considers the same one.
        var referenced = ProjectReferenceEditor.GetProjectReferences(_workingXml)
            .Select(ProjectReferenceEditor.NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var list = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        foreach (var otherPath in otherProjectPaths)
        {
            var relative = ProjectReferenceEditor.NormalizePath(
                Path.GetRelativePath(projectDirectory, otherPath));

            var box = new CheckBox
            {
                Content = Path.GetFileNameWithoutExtension(otherPath),
                IsChecked = referenced.Contains(relative)
            };

            box.IsCheckedChanged += (_, _) => ToggleProjectReference(relative, box.IsChecked == true);

            _projectRows.Add((relative, Path.GetFileNameWithoutExtension(otherPath), box));
            list.Children.Add(box);
        }

        if (otherProjectPaths.Count == 0)
            list.Children.Add(new TextBlock
            {
                Text = Localizer.Get(StringKeys.ReferencesNoOtherProjects),
                Opacity = 0.7
            });

        return new ScrollViewer { Content = list };
    }

    private void ToggleProjectReference(string relativePath, bool wanted)
    {
        _workingXml = wanted
            ? ProjectReferenceEditor.AddProjectReference(_workingXml, relativePath)
            : ProjectReferenceEditor.RemoveProjectReference(_workingXml, relativePath);
    }

    /// <summary>For tests: whether the box for a given project path is checked.</summary>
    internal bool IsProjectChecked(string otherProjectPath)
    {
        var relative = ProjectReferenceEditor.NormalizePath(
            Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(_projectPath))!, otherProjectPath));

        return _projectRows.FirstOrDefault(r => string.Equals(r.RelativePath, relative, StringComparison.Ordinal))
            .Box?.IsChecked == true;
    }

    /// <summary>For tests: checks or unchecks the box for a given project path, as a click would.</summary>
    internal void SetProjectChecked(string otherProjectPath, bool value)
    {
        var relative = ProjectReferenceEditor.NormalizePath(
            Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(_projectPath))!, otherProjectPath));

        var row = _projectRows.FirstOrDefault(r => string.Equals(r.RelativePath, relative, StringComparison.Ordinal));
        if (row.Box is not null) row.Box.IsChecked = value;
    }

    // Assemblies

    private Control BuildAssembliesPage()
    {
        var browse = new Button { Content = Localizer.Get(StringKeys.ReferencesBrowse) };
        var remove = new Button { Content = Localizer.Get(StringKeys.ReferencesRemove), Margin = new Thickness(8, 0, 0, 0) };

        browse.Click += (_, _) => _ = BrowseForAssemblyAsync();
        remove.Click += (_, _) => RemoveSelectedAssembly();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { browse, remove }
        };

        return new StackPanel { Spacing = 0, Children = { _assemblies, buttons } };
    }

    private async Task BrowseForAssemblyAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Get(StringKeys.ReferencesBrowseTitle),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Assemblies") { Patterns = ["*.dll"] }]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;

        AddAssembly(path);
    }

    /// <summary>For tests: adds an assembly reference as Browse would, without a real file picker.</summary>
    internal void AddAssembly(string dllPath)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(_projectPath))!;
        var hintPath = ProjectReferenceEditor.NormalizePath(Path.GetRelativePath(projectDirectory, dllPath));
        var name = Path.GetFileNameWithoutExtension(dllPath);

        _workingXml = ProjectReferenceEditor.AddAssemblyReference(_workingXml, name, hintPath);
        RefreshAssemblies();
    }

    private void RemoveSelectedAssembly()
    {
        if (_assemblies.SelectedItem is not ProjectReferenceEditor.AssemblyReference selected) return;

        _workingXml = ProjectReferenceEditor.RemoveAssemblyReference(_workingXml, selected.Include);
        RefreshAssemblies();
    }

    private void RefreshAssemblies() =>
        _assemblies.ItemsSource = ProjectReferenceEditor.GetAssemblyReferences(_workingXml)
            .Select(r => (object)r)
            .ToList();

    // Packages

    private Control BuildPackagesPage()
    {
        var add = new Button { Content = Localizer.Get(StringKeys.ReferencesAdd) };
        var remove = new Button { Content = Localizer.Get(StringKeys.ReferencesRemove), Margin = new Thickness(8, 0, 0, 0) };

        add.Click += (_, _) => AddPackageFromFields();
        remove.Click += (_, _) => RemoveSelectedPackage();

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,Auto,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetColumn(_packageVersion, 1);
        Grid.SetColumn(add, 2);
        Grid.SetColumn(remove, 3);
        row.Children.Add(_packageId);
        row.Children.Add(_packageVersion);
        row.Children.Add(add);
        row.Children.Add(remove);

        return new StackPanel { Children = { _packages, row, _packageMessage } };
    }

    /// <summary>For tests: types into the package id and version fields, as the user would before clicking Add.</summary>
    internal void SetPackageFieldsForTests(string id, string version)
    {
        _packageId.Text = id;
        _packageVersion.Text = version;
    }

    /// <summary>For tests: adds a package as the Add button would, from the typed id and version.</summary>
    internal void AddPackageFromFields()
    {
        var id = (_packageId.Text ?? "").Trim();
        if (id.Length == 0) return;

        var version = (_packageVersion.Text ?? "").Trim();

        // Without central package management the reference carries its own
        // version, and a blank one would float to the newest package. Refuse it
        // and say why, rather than writing "*".
        if (version.Length == 0 && !_centralPackageManagement)
        {
            _packageMessage.Text = Localizer.Get(StringKeys.PackageVersionRequired);
            _packageMessage.IsVisible = true;
            return;
        }

        // Under central package management Directory.Packages.props owns
        // the version: a new reference is written without one, and one
        // already there is left exactly as it was — either way the message
        // already on screen is what tells the user why, rather than this
        // quietly doing nothing.
        _workingXml = ProjectReferenceEditor.AddPackageReference(
            _workingXml, id, version, _centralPackageManagement);
        _packageId.Text = "";
        _packageVersion.Text = "";
        RefreshPackages();
    }

    private void RemoveSelectedPackage()
    {
        if (_packages.SelectedItem is not ProjectReferenceEditor.PackageEntry selected) return;

        _workingXml = ProjectReferenceEditor.RemovePackageReference(_workingXml, selected.Id);
        RefreshPackages();
    }

    private void RefreshPackages() =>
        _packages.ItemsSource = ProjectReferenceEditor.GetPackageReferences(_workingXml)
            .Select(r => (object)r)
            .ToList();

    // OK / Cancel

    private void Accept()
    {
        AcceptedXml = _workingXml;
        _applyXml(_workingXml);
        Close(true);
    }

    /// <summary>For tests: the text the dialog would write, whether or not OK was actually clicked.</summary>
    internal string WorkingXmlForTests => _workingXml;

    /// <summary>For tests: whether the dialog detected central package management for this project.</summary>
    internal bool CentralPackageManagementForTests => _centralPackageManagement;

    /// <summary>For tests: whether the Packages tab's "managed centrally" notice is showing.</summary>
    internal bool PackageManagedCentrallyMessageVisibleForTests => _packageMessage.IsVisible;

    /// <summary>
    /// The default write-back for OK when no open editor tab claims the
    /// file: straight to disk, preserving a UTF-8 byte-order mark if the
    /// file already carried one. File.ReadAllText/WriteAllText round-trip
    /// the text either way, but WriteAllText's own default encoding has no
    /// preamble, so a BOM the file started with would otherwise vanish on
    /// the very first edit this dialog makes.
    /// </summary>
    private static Action<string> CreateDiskWriter(string projectPath)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: HasUtf8Bom(projectPath));
        return text => File.WriteAllText(projectPath, text, encoding);
    }

    private static bool HasUtf8Bom(string path)
    {
        if (!File.Exists(path)) return false;

        using var stream = File.OpenRead(path);
        Span<byte> preamble = stackalloc byte[3];
        var read = stream.Read(preamble);
        return read == 3 && preamble[0] == 0xEF && preamble[1] == 0xBB && preamble[2] == 0xBF;
    }

    /// <summary>
    /// Central package management, the way MSBuild itself turns it on: this
    /// project's own &lt;ManagePackageVersionsCentrally&gt; (rare — usually
    /// set once in a shared Directory.Build.props, which is already folded
    /// into the project by the time MSBuild evaluates it, but this dialog
    /// only ever sees this one file's text) or a Directory.Packages.props
    /// anywhere from the project's own folder up to the drive root.
    /// </summary>
    /// <summary>
    /// Whether the project's package versions are managed centrally, by its own
    /// property or by a Directory.Packages.props above it.
    /// </summary>
    internal static bool DetectsCentralPackageManagement(string projectPath, string projectXml)
    {
        if (ProjectReferenceEditor.DeclaresCentralPackageManagement(projectXml)) return true;

        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "Directory.Packages.props"))) return true;
            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    /// <summary>
    /// What the Project menu and the Solution Explorer's project node both
    /// call: finds the solution's other projects, opens the dialog, and —
    /// when it comes back with OK — applies the result either to the
    /// project file's own open editor tab (one undoable change there, see
    /// the constructor) or straight to disk followed by the same off-thread
    /// language service reload a rename or a delete already uses.
    ///
    /// Kept here rather than in MainWindow.axaml.cs, whose menu wiring is
    /// meant to stay a one-line call: this is where the rest of that call
    /// belongs.
    /// </summary>
    public static async Task ShowForProjectAsync(
        Window owner, MainWindowViewModel viewModel, string? projectPath)
    {
        if (projectPath is null)
        {
            viewModel.StatusMessage = Localizer.Get(StringKeys.StatusOpenProjectFirst);
            return;
        }

        var otherProjects = OtherProjectsOf(viewModel, projectPath);

        var openDocument = viewModel.OpenDocuments.FirstOrDefault(d =>
            string.Equals(Path.GetFullPath(d.FilePath), Path.GetFullPath(projectPath), PathComparison));

        var dialog = new ReferencesDialog(projectPath, otherProjects,
            openDocument is null ? null : text => openDocument.Text = text);

        var accepted = await dialog.ShowDialog<bool>(owner);
        if (!accepted) return;

        // The open tab's own binding already carries the new text; what is
        // missing is Roslyn (and, for a project reference, the build)
        // seeing it, which only reading the project file back off disk
        // gives — the same reload a Solution Explorer rename or delete
        // already relies on.
        await viewModel.ReloadLanguageServiceAsync();
    }

    /// <summary>Every other project in the open solution, found the same way Solution Explorer finds them.</summary>
    private static IReadOnlyList<string> OtherProjectsOf(MainWindowViewModel viewModel, string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        var results = new List<string>();

        void Walk(SolutionTreeNode node)
        {
            if (node.Kind == NodeKind.Project && !string.Equals(Path.GetFullPath(node.Path), full, PathComparison))
                results.Add(node.Path);

            foreach (var child in node.Children) Walk(child);
        }

        foreach (var root in viewModel.Explorer.Roots) Walk(root);

        return results;
    }

    /// <summary>On Windows and macOS paths are case-insensitive, on Linux they are not.</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
