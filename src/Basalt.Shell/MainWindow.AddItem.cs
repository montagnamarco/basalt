using Avalonia.Platform.Storage;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Workspace;

namespace Basalt.Shell;

/// <summary>
/// Add New Item, from Solution Explorer's menu and from Project, and the file
/// types File &gt; Open offers.
///
/// The text of each item lives in <see cref="ItemTemplates"/>, the name
/// rules in <see cref="ItemTemplates.FileNameFor"/>, and the dialog in
/// <see cref="AddItemDialog"/>. What stays here is choosing the folder,
/// writing the files and opening them.
/// </summary>
public partial class MainWindow
{
    /// <summary>The language registry's id for QuickBASIC, whose programs File &gt; Open must list.</summary>
    private const string QuickBasicLanguageId = "quickbasic";

    /// <summary>
    /// Project &gt; Add New Item…: into the folder of the selected node, or
    /// else of the project the tree shows, the one Add Window acts on.
    /// </summary>
    private Task AddNewItemFromMenuAsync()
    {
        if (ViewModel.Explorer.SelectedNode is { } selected)
            return AddNewItemAsync(DirectoryFor(selected));

        if (SelectedProjectPath() is not { } project)
        {
            ViewModel.StatusMessage = Localizer.Get(StringKeys.StatusOpenProjectFirst);
            return Task.CompletedTask;
        }

        return AddNewItemAsync(Path.GetDirectoryName(Path.GetFullPath(project)));
    }

    /// <summary>
    /// Asks which item to add and writes it into <paramref name="directory"/>,
    /// then opens it: a Windows Form or an Avalonia window in the designer, the
    /// rest in the editor. It appears in Solution Explorer as the other new
    /// files do.
    /// </summary>
    private async Task AddNewItemAsync(string? directory)
    {
        if (directory is not { Length: > 0 }) return;

        // Every template is offered; the ones that do not fit the project the
        // folder belongs to are marked in the dialog, and warned about when
        // added, rather than hidden. ProjectContaining looks from a file's
        // folder upwards, so a placeholder name inside the folder is what it
        // is given.
        var project = ExplorerFileOperations.ProjectContaining(Path.Combine(directory, "_"));
        var profile = ItemProjectProfile.FromProjectFile(project);

        var memory = AddItemMemory.PathBeside(_settingsStore.FilePath);
        var dialog = new AddItemDialog(ItemTemplates.All, profile, directory, AddItemMemory.Load(memory));
        var choice = await dialog.ShowDialog<AddItemChoice?>(this);
        AddItemMemory.Save(memory, dialog.Category);
        if (choice is null) return;

        var files = ItemTemplates.FilesFor(choice.Template, choice.FileName);

        // Every file is checked before the first is written, so a Form whose
        // Designer.vb is already there is refused rather than half made. The
        // dialog checked as the name was typed; this is for what changed since.
        var conflict = files
            .Select(file => (file.FileName, Problem: ExplorerFileOperations.Validate(file.FileName, directory)))
            .FirstOrDefault(p => p.Problem != ExplorerFileOperations.NameProblem.None);

        if (conflict.Problem != ExplorerFileOperations.NameProblem.None)
        {
            ViewModel.WriteOutput(DescribeNameProblem(conflict.Problem, conflict.FileName));
            return;
        }

        string created;
        try
        {
            created = ExplorerFileOperations.CreateFile(directory, files[0].FileName, files[0].Content);

            foreach (var file in files.Skip(1))
                ExplorerFileOperations.CreateFile(directory, file.FileName, file.Content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.WriteOutput(Localizer.Get(StringKeys.ExplorerOperationFailed, ex.Message));
            return;
        }

        ViewModel.Explorer.Refresh();

        // Added all the same: the file is the user's to keep, and a project that
        // lacks the package or the SDK for it is one line away from having it.
        if (choice.Misfit is { } missing)
        {
            var warning = Localizer.Get(StringKeys.AddItemMisfitWarning,
                choice.FileName, AddItemDialog.Describe(missing));
            ViewModel.WriteOutput(warning);
            ViewModel.StatusMessage = warning;
        }
        else
        {
            ViewModel.StatusMessage = Localizer.Get(StringKeys.AddItemAdded, choice.FileName);
        }

        // A form opens in the designer from its Designer.vb, as the WinForms
        // project template opens the MainForm it writes; an Avalonia window
        // from its .axaml; the other items open in the editor.
        if (files.FirstOrDefault(f => f.FileName.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase))
            is { } designer)
        {
            await ViewModel.OpenFileAsync(Path.Combine(directory, designer.FileName), inDesigner: true);
        }
        else if (created.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
        {
            await ViewModel.OpenFileAsync(created, inDesigner: true);
        }
        else
        {
            await ViewModel.OpenFileAsync(created);
        }
    }

    /// <summary>
    /// The file types File &gt; Open offers. The first is the one the dialog
    /// starts on: the solutions and projects, and, only with the legacy switch,
    /// the QuickBASIC extensions the registry claims, so a lone .bas file can be
    /// opened from it as from the Recent list. The Visual Basic 6 project filter
    /// is also legacy-only.
    /// </summary>
    internal IReadOnlyList<FilePickerFileType> SolutionOpenFilters()
    {
        var patterns = new List<string> { "*.sln", "*.slnx", "*.csproj", "*.vbproj" };

        var filters = new List<FilePickerFileType>();

        if (Features.LegacyBasic)
        {
            var programs = Languages.ById(QuickBasicLanguageId)?.Identity.FileExtensions ?? [];
            patterns.AddRange(programs.Select(extension => "*" + extension));
        }

        filters.Add(new FilePickerFileType(Localizer.Get(StringKeys.OpenFilterSolutions)) { Patterns = patterns });

        // A Visual Basic 6 project opens by being converted, and a filter that
        // does not offer it means nobody finds out that it can be.
        if (Features.LegacyBasic)
            filters.Add(new FilePickerFileType("Visual Basic 6 projects") { Patterns = ["*.vbp"] });

        return filters;
    }
}
