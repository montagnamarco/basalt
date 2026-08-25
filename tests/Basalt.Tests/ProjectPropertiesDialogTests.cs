using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell;
using Basalt.Workspace.Projects;

namespace Basalt.Tests;

/// <summary>The properties window, driven against a real project file.</summary>
public sealed class ProjectPropertiesDialogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-propsui", Guid.NewGuid().ToString("N"));

    public ProjectPropertiesDialogTests() => Directory.CreateDirectory(_root);

    private string CreateProject()
    {
        var path = Path.Combine(_root, "App.vbproj");

        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <OptionStrict>On</OptionStrict>
              </PropertyGroup>
            </Project>
            """);

        return path;
    }

    [AvaloniaFact]
    public void OpensOnAProjectAndShowsItsName()
    {
        var dialog = ProjectPropertiesDialog.For(CreateProject());

        Assert.Equal("App", dialog.FindControl<TextBlock>("ProjectNameLabel")!.Text);
    }

    [AvaloniaFact]
    public void ListsEveryCategory()
    {
        var dialog = ProjectPropertiesDialog.For(CreateProject());
        var categories = dialog.FindControl<ListBox>("CategoryList")!;

        var names = categories.Items.OfType<ListBoxItem>()
            .Select(i => i.Content as string)
            .ToList();

        Assert.Contains("Application", names);
        Assert.Contains("Compile", names);
        Assert.Contains("References", names);
        Assert.Contains("Debug", names);
        Assert.Contains("Publish", names);
    }

    [AvaloniaFact]
    public void ShowsTheApplicationPageFirst()
    {
        var dialog = ProjectPropertiesDialog.For(CreateProject());
        dialog.Show();

        var host = dialog.FindControl<ContentControl>("PageHost")!;
        var labels = host.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();

        Assert.Contains("Assembly name", labels);
        Assert.Contains("Root namespace", labels);
    }

    [AvaloniaFact]
    public void ShowsVisualBasicOptionsOnTheCompilePage()
    {
        var dialog = ProjectPropertiesDialog.For(CreateProject());
        dialog.Show();

        var categories = dialog.FindControl<ListBox>("CategoryList")!;
        categories.SelectedIndex = 1;
        dialog.UpdateLayout();

        var host = dialog.FindControl<ContentControl>("PageHost")!;
        var text = string.Join("|", host.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text));
        var checkboxes = host.GetVisualDescendants().OfType<CheckBox>()
            .Select(c => c.Content as string).ToList();

        Assert.Contains("Option Strict", text);
        Assert.Contains("Option Explicit", checkboxes);
        Assert.Contains("Option Infer", checkboxes);
    }

    [AvaloniaFact]
    public async Task WritesChangesBackToTheProjectFile()
    {
        var path = CreateProject();

        var properties = ProjectProperties.Load(path);
        properties.OptionStrict = OptionStrict.Custom;
        properties.AssemblyName = "Renamed";
        await properties.SaveAsync();

        var xml = await File.ReadAllTextAsync(path);

        Assert.Contains("<OptionStrict>Custom</OptionStrict>", xml);
        Assert.Contains("<AssemblyName>Renamed</AssemblyName>", xml);

        // The deliberate empty root namespace must still be there.
        Assert.Contains("<RootNamespace></RootNamespace>", xml);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
