using System.Xml;
using System.Xml.Linq;
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
    /// then opens it: a Windows Form in the designer, the rest in the editor.
    /// It appears in Solution Explorer as the other new files do.
    /// </summary>
    private async Task AddNewItemAsync(string? directory)
    {
        if (directory is not { Length: > 0 }) return;

        // Only a project that builds Windows Forms can build a Form or a
        // UserControl, so those are offered only there. The project is the one
        // the folder belongs to; a folder with none offers the plain items.
        // ProjectContaining looks from a file's folder upwards, so a
        // placeholder name inside the folder is what it is given.
        var project = ExplorerFileOperations.ProjectContaining(Path.Combine(directory, "_"));
        var templates = Enum.GetValues<ItemTemplate>()
            .Where(t => !ItemTemplates.NeedsWindowsForms(t) || UsesWindowsForms(project))
            .ToList();

        var dialog = new AddItemDialog(templates);
        var choice = await dialog.ShowDialog<AddItemChoice?>(this);
        if (choice is null) return;

        var files = ItemTemplates.FilesFor(choice.Template, choice.FileName);

        // Every file is checked before the first is written, so a Form whose
        // Designer.vb is already there is refused rather than half made.
        var problem = files
            .Select(file => ExplorerFileOperations.Validate(file.FileName, directory))
            .FirstOrDefault(p => p != ExplorerFileOperations.NameProblem.None);

        if (problem != ExplorerFileOperations.NameProblem.None)
        {
            ViewModel.WriteOutput(DescribeNameProblem(problem, choice.FileName));
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

        // A form opens in the designer from its Designer.vb, as the WinForms
        // project template opens the MainForm it writes; the other items open
        // in the editor.
        if (files.FirstOrDefault(f => f.FileName.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase))
            is { } designer)
        {
            await ViewModel.OpenFileAsync(Path.Combine(directory, designer.FileName), inDesigner: true);
        }
        else
        {
            await ViewModel.OpenFileAsync(created);
        }
    }

    /// <summary>
    /// Whether a project builds Windows Forms, the way the WinForms project
    /// template says so: UseWindowsForms, a Windows target framework, or the
    /// System.Windows.Forms import. A project that cannot be read builds
    /// nothing with Windows Forms, so it offers none.
    /// </summary>
    private static bool UsesWindowsForms(string? projectPath)
    {
        if (projectPath is null || !File.Exists(projectPath)) return false;

        try
        {
            return XDocument.Load(projectPath).Descendants().Any(element => element.Name.LocalName switch
            {
                "UseWindowsForms" => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase),
                "TargetFramework" or "TargetFrameworks" => element.Value.Contains("-windows", StringComparison.OrdinalIgnoreCase),
                "Import" => string.Equals((string?)element.Attribute("Include"), "System.Windows.Forms", StringComparison.OrdinalIgnoreCase),
                _ => false
            });
        }
        catch (XmlException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The file types File &gt; Open offers. The first is the one the dialog
    /// starts on, so it carries the QuickBASIC extensions the registry claims
    /// as well as the solutions and projects: a lone .bas file is opened from
    /// it, the same as from the Recent list.
    /// </summary>
    internal IReadOnlyList<FilePickerFileType> SolutionOpenFilters()
    {
        var patterns = new List<string> { "*.sln", "*.slnx", "*.csproj", "*.vbproj" };

        var programs = Languages.ById(QuickBasicLanguageId)?.Identity.FileExtensions ?? [];
        patterns.AddRange(programs.Select(extension => "*" + extension));

        return
        [
            new FilePickerFileType(Localizer.Get(StringKeys.OpenFilterSolutions)) { Patterns = patterns },

            // A Visual Basic 6 project opens by being converted, and a
            // filter that does not offer it means nobody finds out that
            // it can be.
            new FilePickerFileType("Visual Basic 6 projects") { Patterns = ["*.vbp"] }
        ];
    }
}
