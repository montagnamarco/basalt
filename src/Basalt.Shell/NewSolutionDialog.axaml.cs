using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Basalt.Core.Features;
using Basalt.Core.Model;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Shell.Controls;

namespace Basalt.Shell;

/// <summary>
/// What the user chose, validated as well-formed but not yet written to
/// disk.
///
/// Collecting these is a separate step from <see cref="NewSolutionDialog.CreateAsync"/>
/// creating the solution from them: see <see cref="MainWindow.ShowNewSolutionDialogAsync"/>
/// for why the dialog itself never touches disk.
/// </summary>
/// <param name="SolutionName">
/// Names the root folder and the .sln file; defaults to <paramref name="Name"/>
/// in the dialog until the user edits it separately, the way Visual Studio's
/// own "Solution name" field does.
/// </param>
public sealed record NewSolutionParameters(
    string ParentDirectory,
    string Name,
    ProjectTemplate Template,
    string SolutionName,
    bool PlaceProjectInSolutionDirectory);

/// <summary>Collects the data for a new solution; creating it on disk is a separate step.</summary>
public partial class NewSolutionDialog : Window
{
    /// <summary>
    /// One selectable leaf of the tree: a project template, with what the
    /// left-hand panel shows once it is chosen.
    /// </summary>
    private sealed record TemplateLeaf(
        ProjectTemplate Template, string NameKey, string DescriptionKey, string TargetKey, IconKind Icon)
        : TemplateTreeNode;

    /// <summary>
    /// A branch of the tree: a dialect or a category, holding either more
    /// branches or leaves. Never selectable itself — selecting one leaves
    /// <see cref="SelectedTemplate"/> null and Create disabled, because a
    /// heading is not a template.
    /// </summary>
    private sealed record TemplateCategory(string NameKey, IReadOnlyList<TemplateTreeNode> Children)
        : TemplateTreeNode;

    private abstract record TemplateTreeNode;

    /// <summary>
    /// Visual Basic .NET, the dialect every user is offered: its leaves group
    /// under categories.
    /// </summary>
    private static readonly TemplateCategory VisualBasicNetDialect =
        new(StringKeys.NewSolutionDialectVisualBasicNet,
        [
            new TemplateCategory(StringKeys.NewSolutionCategoryWindowsDesktop,
            [
                new TemplateLeaf(ProjectTemplate.WinFormsApp, StringKeys.TemplateWinFormsApp,
                    StringKeys.TemplateWinFormsAppDescription, StringKeys.NewSolutionTargetWinFormsApp, IconKind.Window),
                new TemplateLeaf(ProjectTemplate.AvaloniaApp, StringKeys.TemplateAvaloniaApp,
                    StringKeys.TemplateAvaloniaAppDescription, StringKeys.NewSolutionTargetAvaloniaApp, IconKind.DesignerFile),
            ]),
            new TemplateLeaf(ProjectTemplate.ConsoleApp, StringKeys.TemplateConsoleApp,
                StringKeys.TemplateConsoleAppDescription, StringKeys.NewSolutionTargetConsoleApp, IconKind.Terminal),
            new TemplateLeaf(ProjectTemplate.ClassLibrary, StringKeys.TemplateClassLibrary,
                StringKeys.TemplateClassLibraryDescription, StringKeys.NewSolutionTargetClassLibrary, IconKind.Project),
            new TemplateLeaf(ProjectTemplate.TestProject, StringKeys.TemplateTestProject,
                StringKeys.TemplateTestProjectDescription, StringKeys.NewSolutionTargetTestProject, IconKind.Tests),
            new TemplateCategory(StringKeys.NewSolutionCategoryWeb,
            [
                new TemplateLeaf(ProjectTemplate.WebApi, StringKeys.TemplateWebApi,
                    StringKeys.TemplateWebApiDescription, StringKeys.NewSolutionTargetWebApi, IconKind.WebFile),
                new TemplateLeaf(ProjectTemplate.WebApp, StringKeys.TemplateWebApp,
                    StringKeys.TemplateWebAppDescription, StringKeys.NewSolutionTargetWebApp, IconKind.WebFile),
                new TemplateLeaf(ProjectTemplate.RazorPages, StringKeys.TemplateRazorPages,
                    StringKeys.TemplateRazorPagesDescription, StringKeys.NewSolutionTargetRazorPages, IconKind.RazorFile),
                new TemplateLeaf(ProjectTemplate.Mvc, StringKeys.TemplateMvc,
                    StringKeys.TemplateMvcDescription, StringKeys.NewSolutionTargetMvc, IconKind.WebFile),
                new TemplateLeaf(ProjectTemplate.Blazor, StringKeys.TemplateBlazor,
                    StringKeys.TemplateBlazorDescription, StringKeys.NewSolutionTargetBlazor, IconKind.RazorFile),
            ]),
        ]);

    /// <summary>
    /// The legacy dialects, each a single leaf: a category above a lone
    /// project kind would be a click with nothing to choose. Offered only with
    /// <see cref="IdeFeatures.LegacyBasic"/>.
    /// </summary>
    private static readonly IReadOnlyList<TemplateCategory> LegacyBasicDialects =
    [
        new TemplateCategory(StringKeys.NewSolutionDialectVisualBasic6,
        [
            new TemplateLeaf(ProjectTemplate.VisualBasic6, StringKeys.TemplateVisualBasic6,
                StringKeys.TemplateVisualBasic6Description, StringKeys.NewSolutionTargetVisualBasic6, IconKind.VisualBasicFile),
        ]),
        new TemplateCategory(StringKeys.NewSolutionDialectQuickBasic,
        [
            new TemplateLeaf(ProjectTemplate.QuickBasic, StringKeys.TemplateQuickBasic,
                StringKeys.TemplateQuickBasicDescription, StringKeys.NewSolutionTargetQuickBasic, IconKind.ScriptFile),
        ]),
    ];

    /// <summary>The dialects this dialog offers: Visual Basic .NET, and the legacy ones only when switched on.</summary>
    private static IReadOnlyList<TemplateCategory> DialectsFor(IdeFeatures features) =>
        features.LegacyBasic
            ? [VisualBasicNetDialect, .. LegacyBasicDialects]
            : [VisualBasicNetDialect];

    /// <summary>
    /// Characters a solution name cannot contain, so the folder and the
    /// .sln file it names stay valid wherever the solution travels.
    /// <see cref="Path.GetInvalidFileNameChars"/> alone is not enough: on
    /// macOS and Linux it allows every character Windows reserves, so a
    /// name typed there would only fail once the project reached Windows.
    /// </summary>
    private static readonly char[] InvalidSolutionNameChars =
        Path.GetInvalidFileNameChars().Concat(":*?\"<>|").Distinct().ToArray();

    /// <summary>
    /// Device names Windows reserves regardless of extension (CON.txt is as
    /// unusable as CON); compared case-insensitively, the way Windows itself
    /// treats them.
    /// </summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Whether a solution name is safe to use, verbatim, as a folder name
    /// and as the .sln file name on every platform the project might reach
    /// — the same checks Visual Studio's own "Solution name" field makes.
    /// </summary>
    private static bool IsValidSolutionName(string name) =>
        name.Length > 0
        && name.IndexOfAny(InvalidSolutionNameChars) < 0
        && name[^1] is not ('.' or ' ')
        && !ReservedDeviceNames.Contains(name);

    /// <summary>
    /// Whether the solution name box still mirrors <see cref="NameBox"/>.
    /// True until the user types into it directly, the way Visual Studio's
    /// own "Solution name" field follows "Name".
    /// </summary>
    private bool _solutionNameFollowsName = true;

    /// <summary>
    /// Set while this class itself is writing <see cref="SolutionNameBox"/>,
    /// so that mirroring assignment is not mistaken for the user editing it
    /// (which would turn <see cref="_solutionNameFollowsName"/> off at once).
    /// </summary>
    private bool _updatingSolutionName;

    public NewSolutionDialog() : this(IdeFeatures.Default) { }

    /// <summary>
    /// The dialog with the dialects the given features offer. Tests set the
    /// legacy switch here rather than through a static.
    /// </summary>
    public NewSolutionDialog(IdeFeatures features)
    {
        InitializeComponent();

        NameBox.Text = "MyApplication";
        PathBox.Text = DefaultParentDirectory();
        SolutionNameBox.Text = NameBox.Text;

        // PropertyChanged rather than TextChanged: it fires for every
        // assignment to Text, including a test's or a caller's direct
        // assignment, where TextChanged (meant for user editing) is not
        // guaranteed to.
        NameBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;

            if (_solutionNameFollowsName)
            {
                _updatingSolutionName = true;
                SolutionNameBox.Text = NameBox.Text;
                _updatingSolutionName = false;
            }

            UpdatePreview();
        };

        SolutionNameBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;

            if (!_updatingSolutionName) _solutionNameFollowsName = false;
            UpdatePreview();
        };

        PathBox.TextChanged += (_, _) => UpdatePreview();
        SameDirectoryCheck.IsCheckedChanged += (_, _) => UpdatePreview();

        TemplateTree.ItemsSource = DialectsFor(features).Select(BuildNode).ToList();
        TemplateTree.SelectionChanged += (_, _) => UpdateTemplateInfo();

        // Windows Forms, in the spirit of Visual Basic 6: the dialog opens
        // ready to create the one project every dialect's own classic tool
        // would have made the easy choice.
        SelectLeaf(ProjectTemplate.WinFormsApp);

        UpdateTemplateInfo();
        UpdatePreview();

        // Focused when the dialog opens, so it can be filled in without
        // reaching for the mouse first.
        Opened += (_, _) =>
        {
            NameBox.Focus();

            // TreeView drops the selection made in the constructor once its
            // control template is applied on attaching to the visual tree
            // (observed directly: SelectedItem and the container's
            // IsSelected both read back false right after Show(), despite
            // having been set correctly a moment before). Reapplying it here,
            // once the tree actually has a template, is what makes it stick
            // — and only here can the leaf's container be scrolled into
            // view, since BringIntoView needs the real visual tree too.
            SelectLeaf(ProjectTemplate.WinFormsApp);
            UpdateTemplateInfo();
        };
    }

    /// <summary>
    /// Suggested folder: "Projects" in the user's home directory, independent of
    /// the platform.
    /// </summary>
    private static string DefaultParentDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Projects");

    /// <summary>Builds the Avalonia tree node for one dialect, category or leaf.</summary>
    private static TreeViewItem BuildNode(TemplateTreeNode node) => node switch
    {
        TemplateCategory category => new TreeViewItem
        {
            Header = new TextBlock
            {
                Text = Localizer.Get(category.NameKey),
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
            },
            IsExpanded = true,
            ItemsSource = category.Children.Select(BuildNode).ToList(),
        },

        TemplateLeaf leaf => new TreeViewItem
        {
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Spacing.Tight,
                Children =
                {
                    new IconView { Kind = leaf.Icon, IconSize = 14 },
                    new TextBlock { Text = Localizer.Get(leaf.NameKey), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                },
            },
            Tag = leaf,
        },

        _ => throw new NotSupportedException($"Unknown tree node: {node}"),
    };

    /// <summary>Selects the leaf for a template, wherever in the tree it sits.</summary>
    private void SelectLeaf(ProjectTemplate template)
    {
        if (Find(TemplateTree.ItemsSource) is { } item)
        {
            item.IsSelected = true;
            TemplateTree.SelectedItem = item;
            item.BringIntoView();
        }

        TreeViewItem? Find(System.Collections.IEnumerable? items)
        {
            if (items is null) return null;

            foreach (var entry in items)
            {
                if (entry is not TreeViewItem item) continue;
                if (item.Tag is TemplateLeaf leaf && leaf.Template == template) return item;
                if (Find(item.ItemsSource) is { } found) return found;
            }

            return null;
        }
    }

    /// <summary>The leaf currently selected, or null when a category is (or nothing is).</summary>
    private TemplateLeaf? SelectedLeaf =>
        TemplateTree.SelectedItem is TreeViewItem { Tag: TemplateLeaf leaf } ? leaf : null;

    /// <summary>
    /// The template the tree currently has selected; null while a dialect or
    /// a category is selected instead of one of its leaves.
    /// </summary>
    internal ProjectTemplate? SelectedTemplate => SelectedLeaf?.Template;

    /// <summary>Refreshes the name, description and target shown above the form, and Create.</summary>
    private void UpdateTemplateInfo()
    {
        var leaf = SelectedLeaf;

        TemplateNameLabel.Text = leaf is null ? "" : Localizer.Get(leaf.NameKey);
        TemplateDescriptionLabel.Text = leaf is null ? "" : Localizer.Get(leaf.DescriptionKey);
        TemplateTargetLabel.Text = leaf is null ? "" : Localizer.Get(leaf.TargetKey);

        CreateButton.IsEnabled = leaf is not null;
    }

    private void UpdatePreview()
    {
        var name = NameBox.Text?.Trim();
        var parent = PathBox.Text?.Trim();
        var solutionName = EffectiveSolutionName();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parent)
            || string.IsNullOrWhiteSpace(solutionName))
        {
            PreviewLabel.Text = "";
            return;
        }

        var root = Path.Combine(parent, solutionName);
        var projectDirectory = SameDirectoryCheck.IsChecked == true ? root : Path.Combine(root, name);

        PreviewLabel.Text = Localizer.Get(StringKeys.NewSolutionPreview, projectDirectory);
    }

    /// <summary>The solution name field's text, falling back to the project name when blank.</summary>
    private string? EffectiveSolutionName()
    {
        var typed = SolutionNameBox.Text?.Trim();
        return string.IsNullOrWhiteSpace(typed) ? NameBox.Text?.Trim() : typed;
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localizer.Get(StringKeys.NewSolutionChooseFolder),
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) PathBox.Text = path;
    }

    // Collects and validates only: writing anything to disk — even the
    // destination folder — happens later, in CreateAsync, once the caller
    // has had its own chance to ask whether starting a new solution is
    // still wanted. Creating it here, before that question, used to leave
    // a solution on disk that was never opened whenever the question was
    // answered "no" (critical-review finding).
    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        ErrorLabel.Text = "";

        var template = SelectedTemplate;
        if (template is null) return; // Create is disabled for this case; defensive only.

        var name = NameBox.Text?.Trim();
        var parent = PathBox.Text?.Trim();
        var solutionName = EffectiveSolutionName();

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorLabel.Text = Localizer.Get(StringKeys.NewSolutionNameRequired);
            return;
        }

        // The name becomes a namespace and a type name, so it has to be a
        // valid Visual Basic identifier.
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            ErrorLabel.Text = Localizer.Get(StringKeys.NewSolutionInvalidName);
            return;
        }

        if (string.IsNullOrWhiteSpace(parent))
        {
            ErrorLabel.Text = Localizer.Get(StringKeys.NewSolutionLocationRequired);
            return;
        }

        solutionName ??= name;

        // SolutionNameBox is used verbatim as the root folder and as the
        // .sln file's name (below), so — unlike NameBox, which only needs
        // to be a valid VB identifier — it needs to be a valid file and
        // folder name on every platform the solution might travel to.
        if (!IsValidSolutionName(solutionName))
        {
            ErrorLabel.Text = Localizer.Get(StringKeys.NewSolutionInvalidSolutionName);
            return;
        }

        var root = Path.Combine(parent, solutionName);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            ErrorLabel.Text = Localizer.Get(StringKeys.NewSolutionFolderNotEmpty, solutionName);
            return;
        }

        var parameters = new NewSolutionParameters(
            parent, name, template.Value, solutionName, SameDirectoryCheck.IsChecked == true);

        // Closing a window shown through ShowDialog hands this back through
        // the awaited Task; a test that never shows it that way (to avoid
        // the dispatcher plumbing a real modal dialog needs) reads it here.
        LastCreatedForTests = parameters;

        Close(parameters);
    }

    /// <summary>What the last successful Create built, for tests.</summary>
    internal NewSolutionParameters? LastCreatedForTests { get; private set; }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    /// <summary>
    /// Creates the solution <paramref name="parameters"/> describes.
    ///
    /// Separate from the dialog itself so the caller controls exactly when
    /// disk is touched — after asking the user whatever it needs to about
    /// leaving the solution currently open, not before.
    /// </summary>
    public static async Task<NewSolutionResult> CreateAsync(
        NewSolutionParameters parameters, CancellationToken ct = default)
    {
        Directory.CreateDirectory(parameters.ParentDirectory);
        return await SolutionTemplates.CreateAsync(
            parameters.ParentDirectory,
            parameters.SolutionName,
            parameters.Template,
            ct,
            projectName: parameters.Name,
            placeProjectInSolutionDirectory: parameters.PlaceProjectInSolutionDirectory);
    }
}
