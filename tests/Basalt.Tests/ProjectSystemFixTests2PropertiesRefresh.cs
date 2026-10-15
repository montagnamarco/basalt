using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Saving the project properties refreshes what depends on the project: the
/// language service reads the new compiler options, the Problems panel is
/// checked again, and the runnable projects are read again. Driven through the
/// real dialog and a real window, with no reopening of the solution.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class ProjectSystemFixTests2PropertiesRefresh : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-props-refresh-").FullName;

    // Each test has a settings file of its own, so the window does not read or write the user's.
    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-props-refresh-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public ProjectSystemFixTests2PropertiesRefresh() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    /// <summary>
    /// A Visual Basic program whose only problem is an implicit narrowing from Double to Integer. Option Strict
    /// Off accepts it; Option Strict On reports BC30512.
    /// </summary>
    private const string ImplicitConversion = """
        Module Program
            Sub Main()
                Dim d As Double = 2.5
                Dim i As Integer = d
                System.Console.WriteLine(i)
            End Sub
        End Module
        """;

    private string WriteProject(string name, string outputType, string optionStrict = "Off")
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);

        var project = Path.Combine(directory, name + ".vbproj");
        File.WriteAllText(project, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <OptionStrict>{optionStrict}</OptionStrict>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "Program.vb"), ImplicitConversion);
        return project;
    }

    private string WriteSolution(params string[] projects)
    {
        const string visualBasic = "F184B08F-C81C-45F6-A57F-5ABD9991F28F";
        var solution = Path.Combine(_root, "Outer.sln");
        var text = "Microsoft Visual Studio Solution File, Format Version 12.00\n";

        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var relative = Path.GetRelativePath(_root, project).Replace('/', '\\');
            var id = Guid.NewGuid().ToString().ToUpperInvariant();
            text += $"Project(\"{{{visualBasic}}}\") = \"{name}\", \"{relative}\", \"{{{id}}}\"\nEndProject\n";
        }

        File.WriteAllText(solution, text);
        return solution;
    }

    /// <summary>Lays out and pumps the interface thread until the condition holds or the time runs out.</summary>
    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int milliseconds = 120_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }

        return condition();
    }

    /// <summary>The combo box of the page that offers this item, found by an item it must hold.</summary>
    private static ComboBox ComboOffering(ProjectPropertiesDialog dialog, string item) =>
        dialog.GetVisualDescendants().OfType<ComboBox>()
            .First(c => c.Items.Cast<object>().Contains(item));

    /// <summary>Presses the dialog's default button, Save, as a person does.</summary>
    private static void PressSave(ProjectPropertiesDialog dialog) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsDefault)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void ShowCompilePage(ProjectPropertiesDialog dialog)
    {
        dialog.FindControl<ListBox>("CategoryList")!.SelectedIndex = 1;
        dialog.UpdateLayout();
    }

    // ------------------------------------------------------------------
    // Option Strict saved in the dialog reaches the Problems panel at once.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ChangingOptionStrictAndSavingReportsTheImplicitConversionWithoutReopening()
    {
        var project = WriteProject("App", "Exe", optionStrict: "Off");
        var program = Path.Combine(Path.GetDirectoryName(project)!, "Program.vb");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.StatusMessage == Localizer.Get(StringKeys.StatusOpened, "App.vbproj")),
            "the project did not finish opening.");

        await viewModel.OpenFileAsync(program);
        var document = viewModel.OpenDocuments.Single(d => d.FilePath == program);
        await viewModel.RefreshDiagnosticsAsync(document);

        // Accepted while Option Strict is Off: the reproduction starts from a clean Problems list.
        Assert.DoesNotContain(viewModel.Diagnostics, d => d.Id == "BC30512");

        // The dialog is the way a person changes it, saved without reopening anything.
        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show(host.Window);
        ShowCompilePage(dialog);
        ComboOffering(dialog, "Custom").SelectedItem = "On";
        PressSave(dialog);

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.Diagnostics.Any(d => d.Id == "BC30512" && d.FilePath == program)),
            "after Option Strict was saved On, the Problems panel did not report BC30512 for the implicit conversion.");
    }

    // ------------------------------------------------------------------
    // A project that becomes an executable is offered to run at once.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task SavingAnOutputTypeRefreshesTheRunnableProjects()
    {
        var app = WriteProject("App", "Exe");
        var library = WriteProject("Lib", "Library");
        var solution = WriteSolution(app, library);

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.StatusMessage == Localizer.Get(StringKeys.StatusOpened, "Outer.sln")),
            "the solution did not finish opening.");

        Assert.Equal([app], viewModel.RunnableProjects);
        Assert.Equal(app, viewModel.StartupProject);

        // Lib becomes an executable in its properties. The Application page holds the output type.
        var dialog = ProjectPropertiesDialog.For(library);
        dialog.Show(host.Window);
        ComboOffering(dialog, "WinExe").SelectedItem = "Exe";
        PressSave(dialog);

        Assert.True(await WaitUntilAsync(host, () => viewModel.RunnableProjects.Contains(library)),
            "the saved project was not offered to run.");
        Assert.Equal(app, viewModel.StartupProject);
    }
}
