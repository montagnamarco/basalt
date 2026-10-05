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
    public void DesktopGuiChoiceSelectsTheRequestedTemplateAndDoesNotAffectConsoleProjects()
    {
        var dialog = new NewSolutionDialog();
        var gui = dialog.FindControl<ComboBox>("GuiChoice")!;
        var templates = dialog.FindControl<ListBox>("TemplateList")!;
        var panel = dialog.FindControl<StackPanel>("GuiPanel")!;
        Assert.Equal(ProjectTemplate.AvaloniaApp, dialog.SelectedTemplate);
        gui.SelectedIndex = 1;
        Assert.Equal(ProjectTemplate.WinFormsApp, dialog.SelectedTemplate);
        templates.SelectedItem = templates.Items.OfType<ListBoxItem>().Single(item => (string?)item.Tag == "ConsoleApp");
        Assert.Equal(ProjectTemplate.ConsoleApp, dialog.SelectedTemplate);
        Assert.False(panel.IsVisible);
        templates.SelectedItem = templates.Items.OfType<ListBoxItem>().Single(item => (string?)item.Tag == "AvaloniaApp");
        Assert.True(panel.IsVisible);
        Assert.Equal(ProjectTemplate.WinFormsApp, dialog.SelectedTemplate);
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
