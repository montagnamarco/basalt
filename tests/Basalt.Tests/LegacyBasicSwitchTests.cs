using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Features;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Languages;

namespace Basalt.Tests;

/// <summary>
/// What a user sees with the legacy Basic switch off, which is the default:
/// no Visual Basic 6 and no QuickBASIC in the New Solution dialog, in File &gt;
/// Open, in the language registry, on the welcome page, or when a .bas or a
/// .vbp is opened.
/// </summary>
/// <remarks>
/// Each hidden-state test has a positive control with the switch on, so a
/// passing assertion cannot be the product of a path that never reaches the
/// legacy feature at all.
/// </remarks>
[Collection(SettingsFileCollection.Name)]
public sealed class LegacyBasicSwitchTests : IDisposable
{
    private static readonly IdeFeatures Legacy = new() { LegacyBasic = true };

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-legacy-switch-" + Guid.NewGuid().ToString("N"));

    public LegacyBasicSwitchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by a window that did not close is not a failure of this test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------------
    // New Solution
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public void TheNewSolutionDialogListsNoVisualBasic6OrQuickBasicDialectOrTemplate()
    {
        var labels = VisibleLabels(new NewSolutionDialog().FindControl<TreeView>("TemplateTree")!);

        Assert.DoesNotContain(Localizer.Get(StringKeys.NewSolutionDialectVisualBasic6), labels);
        Assert.DoesNotContain(Localizer.Get(StringKeys.NewSolutionDialectQuickBasic), labels);
        Assert.DoesNotContain(Localizer.Get(StringKeys.TemplateVisualBasic6), labels);
        Assert.DoesNotContain(Localizer.Get(StringKeys.TemplateQuickBasic), labels);

        // The walk really read the tree: the ordinary dialect's templates are in it.
        Assert.Contains(Localizer.Get(StringKeys.NewSolutionDialectVisualBasicNet), labels);
        Assert.Contains(Localizer.Get(StringKeys.TemplateWinFormsApp), labels);
    }

    [AvaloniaFact]
    public void WithTheSwitchOnTheNewSolutionDialogListsBothLegacyDialects()
    {
        var labels = VisibleLabels(new NewSolutionDialog(Legacy).FindControl<TreeView>("TemplateTree")!);

        Assert.Contains(Localizer.Get(StringKeys.NewSolutionDialectVisualBasic6), labels);
        Assert.Contains(Localizer.Get(StringKeys.NewSolutionDialectQuickBasic), labels);
        Assert.Contains(Localizer.Get(StringKeys.TemplateVisualBasic6), labels);
        Assert.Contains(Localizer.Get(StringKeys.TemplateQuickBasic), labels);
    }

    // ------------------------------------------------------------------
    // File > Open
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task FileOpenOffersNoVbpAndNoBasFile()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);

        var filters = host.Window.SolutionOpenFilters();
        var patterns = filters.SelectMany(f => f.Patterns ?? []).ToList();

        Assert.DoesNotContain("*.vbp", patterns);
        Assert.DoesNotContain("*.bas", patterns);
        Assert.DoesNotContain("*.qb", patterns);
        Assert.DoesNotContain(filters, f => f.Name == "Visual Basic 6 projects");

        // The projects and solutions it always offered are still there.
        Assert.Contains("*.vbproj", patterns);
        Assert.Contains("*.slnx", patterns);
    }

    [AvaloniaFact]
    public async Task WithTheSwitchOnFileOpenOffersVbpAndBas()
    {
        using var host = new TestWindow(features: Legacy);
        await host.SettleAsync(10);

        var filters = host.Window.SolutionOpenFilters();
        var patterns = filters.SelectMany(f => f.Patterns ?? []).ToList();

        Assert.Contains("*.bas", patterns);
        Assert.Contains("*.vbp", patterns);
        Assert.Contains(filters, f => f.Name == "Visual Basic 6 projects");
    }

    // ------------------------------------------------------------------
    // Language registration and opening a .bas file
    // ------------------------------------------------------------------

    [Fact]
    public void TheCatalogRegistersQuickBasicOnlyWithTheSwitch()
    {
        using var roslyn = new RoslynLanguageService();

        var hidden = LanguageCatalog.CreateDefault(roslyn, new RoslynFormattingService());
        var shown = LanguageCatalog.CreateDefault(roslyn, new RoslynFormattingService(), Legacy);

        Assert.Null(hidden.ForFile("/a/game.bas"));
        Assert.Null(hidden.ById("quickbasic"));
        Assert.NotNull(shown.ForFile("/a/game.bas"));
    }

    [AvaloniaFact]
    public async Task OpeningABasFileDoesNotActivateQuickBasic()
    {
        var path = Path.Combine(_root, "game.bas");
        await File.WriteAllTextAsync(path, "PRINT \"hello\"\nEND\n");

        using var vm = new MainWindowViewModel();
        await vm.OpenFileAsync(path);

        // The document opens as an ordinary text file: no language claims it,
        // so nothing colours it, completes it or runs it as QuickBASIC.
        Assert.NotNull(vm.ActiveDocument);
        Assert.Equal(path, vm.ActiveDocument!.FilePath);
        Assert.Null(vm.Languages.ForFile(path));
        Assert.Null(vm.Languages.ById("quickbasic"));
    }

    [AvaloniaFact]
    public async Task WithTheSwitchOnOpeningABasFileActivatesQuickBasic()
    {
        var path = Path.Combine(_root, "game.bas");
        await File.WriteAllTextAsync(path, "PRINT \"hello\"\nEND\n");

        using var vm = new MainWindowViewModel(Legacy);
        await vm.OpenFileAsync(path);

        Assert.NotNull(vm.Languages.ForFile(path));
        Assert.NotNull(vm.Languages.ById("quickbasic"));
    }

    // ------------------------------------------------------------------
    // Opening a Visual Basic 6 project
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AVisualBasic6ProjectIsNotOpenedWhileTheSwitchIsOff()
    {
        var directory = CopyOfVb6Sample();
        var vbp = Path.Combine(directory, "Anagrafica.vbp");

        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(vbp);

        // Refused before anything is converted or closed.
        Assert.Null(vm.SolutionPath);
        Assert.False(File.Exists(Path.Combine(directory, "Anagrafica.vbproj")));
        Assert.Equal(Localizer.Get(StringKeys.StatusOpenFailed, "Anagrafica.vbp"), vm.StatusMessage);
    }

    [AvaloniaFact]
    public async Task WithTheSwitchOnAVisualBasic6ProjectIsConvertedBesideItself()
    {
        var directory = CopyOfVb6Sample();

        using var vm = new MainWindowViewModel(Legacy);
        await vm.OpenSolutionAsync(Path.Combine(directory, "Anagrafica.vbp"));

        Assert.True(File.Exists(Path.Combine(directory, "Anagrafica.vbproj")));
    }

    /// <summary>
    /// A copy of the Visual Basic 6 sample, so a conversion writes into the
    /// temporary folder and never into the repository.
    /// </summary>
    private string CopyOfVb6Sample()
    {
        var source = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "Basalt.Sample.Vb6"));
        var target = Path.Combine(_root, "vb6");
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        return target;
    }

    // ------------------------------------------------------------------
    // Solution Explorer
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AFormInASolutionFolderIsAnOrdinaryFileWhileTheSwitchIsOff()
    {
        var (project, form) = await WriteSolutionWithFormAsync();

        using var vm = new MainWindowViewModel();
        vm.Explorer.Load(project);
        var node = Flatten(vm.Explorer.Roots).Single(n => n.Name == "Form1.frm");

        // Shown as a file, not as a designable form: no designer icon, and the
        // "Open in Designer" menu item is offered only for IsDesignable nodes.
        Assert.Equal(NodeKind.Other, node.Kind);
        Assert.False(node.IsDesignable);
        Assert.Null(node.DesignerPath);

        // A double-click opens it as text, not in the designer.
        await vm.OpenExplorerNodeAsync(node);
        Assert.NotNull(vm.ActiveDocument);
        Assert.Equal(form, vm.ActiveDocument!.FilePath);
        Assert.False(vm.ActiveDocument.OpenInDesigner);
    }

    [AvaloniaFact]
    public async Task WithTheSwitchOnAFormInASolutionFolderIsDesignable()
    {
        var (project, _) = await WriteSolutionWithFormAsync();

        using var vm = new MainWindowViewModel(Legacy);
        vm.Explorer.Load(project);
        var node = Flatten(vm.Explorer.Roots).Single(n => n.Name == "Form1.frm");

        Assert.Equal(NodeKind.XamlFile, node.Kind);
        Assert.True(node.IsDesignable);
    }

    /// <summary>A VB.NET project folder with a leftover .frm beside it.</summary>
    private async Task<(string Project, string Form)> WriteSolutionWithFormAsync()
    {
        var directory = Path.Combine(_root, "solution");
        Directory.CreateDirectory(directory);

        var project = Path.Combine(directory, "Legacy.vbproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var form = Path.Combine(directory, "Form1.frm");
        await File.WriteAllTextAsync(form, "VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\n");

        return (project, form);
    }

    private static IEnumerable<SolutionTreeNode> Flatten(IEnumerable<SolutionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    // ------------------------------------------------------------------
    // Welcome page
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TheWelcomePageDoesNotNameTheLegacyDialects()
    {
        using var host = new TestWindow();
        await host.SettleAsync(2);

        var newProject = host.Window.FindControl<TextBlock>("WelcomeNewProjectDescription")!.Text ?? "";
        var openProject = host.Window.FindControl<TextBlock>("WelcomeOpenProjectDescription")!.Text ?? "";

        foreach (var text in new[] { newProject, openProject })
        {
            Assert.DoesNotContain("QuickBASIC", text);
            Assert.DoesNotContain("Visual Basic 6", text);
        }

        Assert.Equal(Localizer.Get(StringKeys.WelcomeNewProjectDescription), newProject);
        Assert.Equal(Localizer.Get(StringKeys.WelcomeOpenProjectDescription), openProject);
    }

    [AvaloniaFact]
    public async Task WithTheSwitchOnTheWelcomePageNamesTheLegacyDialects()
    {
        using var host = new TestWindow(features: Legacy);
        await host.SettleAsync(2);

        var newProject = host.Window.FindControl<TextBlock>("WelcomeNewProjectDescription")!.Text ?? "";
        var openProject = host.Window.FindControl<TextBlock>("WelcomeOpenProjectDescription")!.Text ?? "";

        Assert.Contains("QuickBASIC", newProject);
        Assert.Contains("Visual Basic 6", openProject);
    }

    /// <summary>
    /// Every text the tree shows, headings and leaves alike, as a user reads it.
    /// </summary>
    private static List<string> VisibleLabels(TreeView tree)
    {
        var labels = new List<string>();
        Collect(tree.ItemsSource, labels);
        return labels;

        static void Collect(IEnumerable? items, List<string> into)
        {
            if (items is null) return;

            foreach (var item in items.OfType<TreeViewItem>())
            {
                switch (item.Header)
                {
                    case TextBlock text:
                        into.Add(text.Text ?? "");
                        break;
                    case StackPanel panel:
                        into.AddRange(panel.Children.OfType<TextBlock>().Select(t => t.Text ?? ""));
                        break;
                }

                Collect(item.ItemsSource, into);
            }
        }
    }
}
