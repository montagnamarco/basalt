using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Workspace;

namespace Basalt.Tests;

// Builds a project that references Basalt.Forms in place.
[Collection(WindowsDesktopCollection.Name)]
public sealed class WinFormsSolutionTemplatesTests
{
    [AvaloniaFact]
    public void TheTreesTwoDesktopLeavesSelectWinFormsAndAvalonia()
    {
        // The GUI picker is gone: WinForms and Avalonia are now two separate
        // leaves of the tree, under "Windows Desktop".
        var dialog = new NewSolutionDialog();
        var tree = dialog.FindControl<TreeView>("TemplateTree")!;

        Assert.Equal(ProjectTemplate.WinFormsApp, dialog.SelectedTemplate);

        SelectLeaf(tree, ProjectTemplate.ConsoleApp);
        Assert.Equal(ProjectTemplate.ConsoleApp, dialog.SelectedTemplate);

        SelectLeaf(tree, ProjectTemplate.AvaloniaApp);
        Assert.Equal(ProjectTemplate.AvaloniaApp, dialog.SelectedTemplate);
    }

    private static void SelectLeaf(TreeView tree, ProjectTemplate template)
    {
        var item = Flatten(tree.ItemsSource).Single(i =>
            i.Tag?.GetType().GetProperty("Template")?.GetValue(i.Tag) is ProjectTemplate t && t == template);
        tree.SelectedItem = item;
    }

    private static IEnumerable<TreeViewItem> Flatten(System.Collections.IEnumerable? items)
    {
        if (items is null) yield break;

        foreach (var entry in items)
        {
            if (entry is not TreeViewItem item) continue;
            yield return item;
            foreach (var child in Flatten(item.ItemsSource)) yield return child;
        }
    }

    [Fact]
    public async Task GeneratedWinFormsSolutionBuildsWithThePortableRuntimeAndDesignerDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), "basalt-forms-template-" + Guid.NewGuid().ToString("N"));
        try
        {
            var generated = await SolutionTemplates.CreateAsync(root, "PortableForms", ProjectTemplate.WinFormsApp);
            var project = XDocument.Load(generated.ProjectPath);
            Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
            Assert.Empty(project.Descendants("UseWindowsForms"));
            Assert.Contains(project.Descendants("Import"), element => (string?)element.Attribute("Include") == "System.Windows.Forms");
            Assert.NotNull(generated.MainFormDesignerPath);
            Assert.Null(generated.MainWindowXamlPath);
            var designerSource = await File.ReadAllTextAsync(generated.MainFormDesignerPath);
            var document = new WinFormsDesignerDocument(designerSource);
            var withButton = new WinFormsDesignerDocument(document.AddControl("Button1", "Global.System.Windows.Forms.Button"));
            await File.WriteAllTextAsync(generated.MainFormDesignerPath, withButton.SetProperty("Button1", "Text", "\"Ready\""));
            var companion = Path.Combine(Path.GetDirectoryName(generated.ProjectPath)!, "MainForm.vb");
            var source = await File.ReadAllTextAsync(companion);
            source = source.Replace("End Class", "    Private Sub ShowGreeting()\n        MessageBox.Show(\"Hello\")\n    End Sub\nEnd Class", StringComparison.Ordinal);
            await File.WriteAllTextAsync(companion, source);
            var builder = new MsBuildBuildService();
            var output = new System.Text.StringBuilder();
            builder.OutputReceived += (_, line) => { lock (output) output.AppendLine(line); };
            var result = await builder.BuildAsync(generated.ProjectPath);
            Assert.True(result.Succeeded, output.ToString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
