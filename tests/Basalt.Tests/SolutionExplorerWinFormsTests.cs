using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Input;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class SolutionExplorerWinFormsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("basalt-form-explorer-").FullName;

    private string Write(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "Public Class Form1\nEnd Class\n");
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void FormCompanionsAreNestedAndOrphansStayVisible()
    {
        var project = Write("App.vbproj");
        var form = Write("Form1.vb");
        var designer = Write("Form1.Designer.vb");
        var resources = Write("Form1.resx");
        var italian = Write("Form1.it.resx");
        var regional = Write("Form1.it-IT.resx");
        var unrelated = Write("Form1.Custom.resx");
        var orphan = Write("Orphan.Designer.vb");

        var explorer = new SolutionExplorerViewModel();
        explorer.Load(project);

        var formNode = Assert.Single(explorer.Roots[0].Children, node => node.Path == form);
        Assert.Equal(designer, formNode.DesignerPath);
        Assert.Equal([designer, resources, italian, regional], formNode.Children.Select(node => node.Path));
        Assert.Contains(explorer.Roots[0].Children, node => node.Path == unrelated);
        Assert.Contains(explorer.Roots[0].Children, node => node.Path == orphan);
        Assert.DoesNotContain(explorer.Roots[0].Children, node => node.Path == designer);
    }

    [Fact]
    public void RefreshPreservesTheSelectedCompanionAndExpandedForm()
    {
        var project = Write("App.vbproj");
        Write("Form1.vb");
        var designer = Write("Form1.Designer.vb");
        var explorer = new SolutionExplorerViewModel();
        explorer.Load(project);
        Assert.True(explorer.SelectPath(designer));

        explorer.Refresh();

        Assert.Equal(designer, explorer.SelectedNode?.Path);
        Assert.True(explorer.Roots[0].Children.Single(node => node.IsForm).IsExpanded);
    }

    [AvaloniaFact]
    public async Task SelectingAFormOpensDesignerAndViewCodeOpensHandwrittenFile()
    {
        var project = Write("App.vbproj");
        var form = Write("Form1.vb");
        var designer = Write("Form1.Designer.vb");

        using var viewModel = new MainWindowViewModel();
        viewModel.Explorer.Load(project);
        var formNode = Assert.Single(viewModel.Explorer.Roots[0].Children,
            node => node.Path == form);
        var panel = new SolutionExplorerPanel(viewModel);
        SolutionTreeNode? selected = null;
        panel.FileOpened += (_, node) => selected = node;
        panel.TreeForTests.SelectedItem = formNode;

        Assert.Same(formNode, selected);
        await viewModel.OpenExplorerNodeAsync(selected!);
        Assert.Equal(designer, viewModel.ActiveDocument?.FilePath);
        Assert.True(viewModel.ActiveDocument?.OpenInDesigner);
        Assert.Equal("Form1.vb " + Localizer.Get(StringKeys.WinFormsDesignerDesignTabSuffix),
            viewModel.ActiveDocument?.Title);

        await viewModel.OpenExplorerCodeAsync(formNode);
        Assert.Equal(form, viewModel.ActiveDocument?.FilePath);
        Assert.False(viewModel.ActiveDocument?.OpenInDesigner);

        selected = null;
        panel.TreeForTests.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
        Assert.Same(formNode, selected);
    }

    [AvaloniaFact]
    public void RefreshWithASelectedFormDoesNotOpenItsDesigner()
    {
        var project = Write("App.vbproj");
        var form = Write("Form1.vb");
        Write("Form1.Designer.vb");

        using var viewModel = new MainWindowViewModel();
        viewModel.Explorer.Load(project);
        var panel = new SolutionExplorerPanel(viewModel);
        var formNode = Assert.Single(viewModel.Explorer.Roots[0].Children, node => node.Path == form);

        // The user's own selection still opens the designer: only the
        // selection Refresh restores afterwards must stay silent.
        panel.TreeForTests.SelectedItem = formNode;

        SolutionTreeNode? opened = null;
        panel.FileOpened += (_, node) => opened = node;

        viewModel.Explorer.Refresh();

        Assert.Null(opened);
        Assert.Equal(form, viewModel.Explorer.SelectedNode?.Path);
    }

    [AvaloniaFact]
    public void ShowingAllFilesWithASelectedFormDoesNotOpenItsDesigner()
    {
        var project = Write("App.vbproj");
        var form = Write("Form1.vb");
        Write("Form1.Designer.vb");

        using var viewModel = new MainWindowViewModel();
        viewModel.Explorer.Load(project);
        var panel = new SolutionExplorerPanel(viewModel);
        var formNode = Assert.Single(viewModel.Explorer.Roots[0].Children, node => node.Path == form);

        panel.TreeForTests.SelectedItem = formNode;

        SolutionTreeNode? opened = null;
        panel.FileOpened += (_, node) => opened = node;

        // Toggling "Show All Files" rebuilds the tree and restores the
        // selection the same way Refresh does.
        viewModel.Explorer.ShowAllFiles = true;

        Assert.Null(opened);
        Assert.Equal(form, viewModel.Explorer.SelectedNode?.Path);
    }

    [Fact]
    public void UserFileRoundTripsSelectionAndExactTabModes()
    {
        var solution = Write("App.slnx");
        var designer = Write("Form1.Designer.vb");
        var session = new SolutionSession
        {
            SolutionPath = solution,
            OpenTabs =
            [
                new OpenDocumentSession { Path = designer, InDesigner = false },
                new OpenDocumentSession { Path = designer, InDesigner = true }
            ],
            ActiveFile = designer,
            ActiveInDesigner = true,
            SelectedPath = designer,
            ExpandedFolders = [_directory]
        };

        Assert.True(SolutionUserSessionStore.Write(session));
        var restored = Assert.IsType<SolutionSession>(SolutionUserSessionStore.Read(solution));
        Assert.Equal([false, true], restored.OpenTabs.Select(tab => tab.InDesigner));
        Assert.True(restored.ActiveInDesigner);
        Assert.Equal(designer, restored.SelectedPath);
        Assert.Equal([_directory], restored.ExpandedFolders);
    }

    [Fact]
    public void ProjectSessionIsNotWrittenToTheMsBuildUserFile()
    {
        // Microsoft.Common.CurrentVersion.targets imports
        // "$(MSBuildProjectFullPath).user" while building: a session written
        // there breaks the very next build of that project with MSB4025.
        var project = Write("App.vbproj");
        var designer = Write("Form1.Designer.vb");
        var session = new SolutionSession
        {
            SolutionPath = project,
            SelectedPath = designer
        };

        Assert.True(SolutionUserSessionStore.Write(session));

        Assert.False(File.Exists(project + ".user"));
        Assert.True(File.Exists(project + ".basalt.user"));

        var restored = Assert.IsType<SolutionSession>(SolutionUserSessionStore.Read(project));
        Assert.Equal(designer, restored.SelectedPath);

        // The file sits in the project folder: the explorer must not list it,
        // and it ends in ".user" so the usual gitignore rule matches it.
        Assert.EndsWith(".user", SolutionUserSessionStore.PathFor(project), StringComparison.Ordinal);
        var explorer = new SolutionExplorerViewModel();
        explorer.Load(project);
        Assert.DoesNotContain(explorer.Roots[0].Children,
            node => node.Path == SolutionUserSessionStore.PathFor(project));
    }

    [Fact]
    public void ProjectSessionIsNotReadFromTheMsBuildUserFile()
    {
        // "<project>.user" is MSBuild's own import; a file there is not ours
        // to keep alive, so it is never read as a session.
        var project = Write("App.vbproj");
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            Format = "BasaltSolutionSession",
            Version = 1,
            Session = new SolutionSession { SolutionPath = project, SelectedPath = project }
        });
        File.WriteAllText(project + ".user", json);

        Assert.Null(SolutionUserSessionStore.Read(project));
    }

    [Fact]
    public void ForeignUserFileIsPreserved()
    {
        var solution = Write("App.slnx");
        var userFile = SolutionUserSessionStore.PathFor(solution);
        const string foreign = "<Project><PropertyGroup /></Project>";
        File.WriteAllText(userFile, foreign);

        Assert.False(SolutionUserSessionStore.Write(new SolutionSession
        {
            SolutionPath = solution
        }));
        Assert.Equal(foreign, File.ReadAllText(userFile));
    }

    [AvaloniaFact]
    public async Task RestoreKeepsCodeAndDesignerViewsOfOneFileDistinct()
    {
        var designer = Write("Form1.Designer.vb");
        using var viewModel = new MainWindowViewModel();
        await viewModel.RestoreSessionAsync(new SolutionSession
        {
            OpenTabs =
            [
                new OpenDocumentSession { Path = designer, InDesigner = false },
                new OpenDocumentSession { Path = designer, InDesigner = true }
            ],
            ActiveFile = designer,
            ActiveInDesigner = false
        });

        Assert.Equal([false, true], viewModel.OpenDocuments.Select(tab => tab.OpenInDesigner));
        Assert.False(viewModel.ActiveDocument?.OpenInDesigner);
    }
}
