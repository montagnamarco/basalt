using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Workspace.Projects;

namespace Basalt.Tests;

/// <summary>
/// The Startup object of a project: the types that can start it, and what saving writes into the project file.
/// </summary>
public sealed class ProjectSystemFixTests2StartupObject : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-startup-object-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// A console project with the types the list must sort out: a module with Sub Main (listed), a module with
    /// Sub Main taking the arguments (listed), a module with a Function Main returning Integer (listed), a module with
    /// Function Main taking the arguments (listed), a class with a shared Main (listed), a class with an instance Main
    /// (not listed: it is not shared), a module without a Main (not listed), and a form (listed) that inherits the
    /// Windows Forms Form.
    /// </summary>
    /// <remarks>
    /// Form is declared in Global.System.Windows.Forms here so that it is named as a real Windows Forms form is, without
    /// a reference to the framework; the project's RootNamespace is not put in front of it.
    /// </remarks>
    private string WriteProject(string rootNamespace, string? startupObject = null)
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var project = Path.Combine(directory, "App.vbproj");
        var startup = startupObject is null ? "" : $"\n    <StartupObject>{startupObject}</StartupObject>";
        File.WriteAllText(project, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>{rootNamespace}</RootNamespace>{startup}
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(directory, "Program.vb"), """
            Module Program
                Sub Main()
                    System.Console.WriteLine("hello")
                End Sub
            End Module
            """);
        File.WriteAllText(Path.Combine(directory, "Launcher.vb"), """
            Module Launcher
                Sub Main(args As String())
                End Sub
            End Module
            """);
        File.WriteAllText(Path.Combine(directory, "Helpers.vb"), """
            Module Helpers
                Function Add(a As Integer, b As Integer) As Integer
                    Return a + b
                End Function
            End Module
            """);
        File.WriteAllText(Path.Combine(directory, "Tool.vb"), """
            Class Tool
                Shared Sub Main()
                End Sub
            End Class
            """);
        File.WriteAllText(Path.Combine(directory, "Runner.vb"), """
            Module Runner
                Function Main() As Integer
                    Return 0
                End Function
            End Module
            """);
        File.WriteAllText(Path.Combine(directory, "Starter.vb"), """
            Module Starter
                Function Main(args() As String) As Integer
                    Return 0
                End Function
            End Module
            """);
        File.WriteAllText(Path.Combine(directory, "Instance.vb"), """
            Class Instance
                Sub Main()
                End Sub
            End Class
            """);
        File.WriteAllText(Path.Combine(directory, "Forms.vb"), """
            Namespace Global.System.Windows.Forms
                Public Class Form
                End Class
            End Namespace
            """);
        File.WriteAllText(Path.Combine(directory, "MainForm.vb"), """
            Public Class MainForm
                Inherits System.Windows.Forms.Form
            End Class
            """);

        return project;
    }

    private static string L(string key) => Localizer.Get(key);

    /// <summary>Waits, on the interface thread, until the condition holds or the time runs out.</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int milliseconds = 120_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
        }

        Dispatcher.UIThread.RunJobs();
        return condition();
    }

    /// <summary>The Startup object combo box of the Application page: the one offering the notion of "(Not set)".</summary>
    private static ComboBox StartupObjectBox(ProjectPropertiesDialog dialog) =>
        dialog.GetVisualDescendants().OfType<ComboBox>()
            .First(c => c.Items.Cast<object>().Contains(L(StringKeys.ProjectPropertiesStartupObjectNotSet)));

    /// <summary>Presses the dialog's default button, Save, as a person does.</summary>
    private static void PressSave(ProjectPropertiesDialog dialog) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsDefault)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static List<string> ItemsOf(ComboBox box) => box.Items.Cast<string>().ToList();

    // ------------------------------------------------------------------
    // The list: the modules with a Sub Main and the forms, fully qualified with the RootNamespace.
    // ------------------------------------------------------------------
    [Fact]
    public async Task ListsTheModulesWithAMainAndTheFormsFullyQualified()
    {
        var project = WriteProject("Acme");

        var found = await StartupObjectCatalog.FindAsync(project);

        Assert.Equal(
            ["Acme.Launcher", "Acme.MainForm", "Acme.Program", "Acme.Runner", "Acme.Starter", "Acme.Tool"],
            found);
    }

    [Fact]
    public async Task WithoutARootNamespaceTheNamesHaveNoPrefix()
    {
        var project = WriteProject("");

        var found = await StartupObjectCatalog.FindAsync(project);

        Assert.Equal(["Launcher", "MainForm", "Program", "Runner", "Starter", "Tool"], found);
    }

    // ------------------------------------------------------------------
    // The page: the list in its order, the choice written, and "(Not set)" removing the element.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheApplicationPageOffersNotSetAndTheStartupTypesAndWritesTheChoice()
    {
        var project = WriteProject("Acme");
        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        var box = StartupObjectBox(dialog);
        Assert.True(await WaitUntilAsync(() => ItemsOf(box).Contains("Acme.Program")), "the startup types were not listed.");

        Assert.Equal(
            [
                L(StringKeys.ProjectPropertiesStartupObjectNotSet), "Acme.Launcher", "Acme.MainForm", "Acme.Program",
                "Acme.Runner", "Acme.Starter", "Acme.Tool"
            ],
            ItemsOf(box));

        box.SelectedItem = "Acme.MainForm";
        PressSave(dialog);

        Assert.True(await WaitUntilAsync(() => File.ReadAllText(project).Contains("<StartupObject>", StringComparison.Ordinal)),
            "saving did not write the startup object.");
        Assert.Contains("<StartupObject>Acme.MainForm</StartupObject>", File.ReadAllText(project));
    }

    [AvaloniaFact]
    public async Task ARootNamespaceChangedBeforeSavingIsWrittenInTheStartupObject()
    {
        var project = WriteProject("");
        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        var box = StartupObjectBox(dialog);
        Assert.True(await WaitUntilAsync(() => ItemsOf(box).Contains("Program")), "the startup types were not listed.");
        box.SelectedItem = "Program";

        // The root namespace is typed in the Application page, the box that is empty.
        var root = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "");
        root.Text = "Acme";

        PressSave(dialog);

        Assert.True(await WaitUntilAsync(() => File.ReadAllText(project).Contains("<StartupObject>", StringComparison.Ordinal)),
            "saving did not write the startup object.");
        Assert.Contains("<StartupObject>Acme.Program</StartupObject>", File.ReadAllText(project));
    }

    [AvaloniaFact]
    public async Task NotSetRemovesTheStartupObjectFromTheProjectFile()
    {
        var project = WriteProject("App", startupObject: "App.Program");

        // The file starts with the element, so the test can only pass by removing it.
        Assert.Contains("<StartupObject>App.Program</StartupObject>", File.ReadAllText(project));

        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        var box = StartupObjectBox(dialog);
        Assert.True(await WaitUntilAsync(() => ItemsOf(box).Contains("App.Program")), "the startup types were not listed.");
        Assert.Equal("App.Program", box.SelectedItem);

        box.SelectedItem = L(StringKeys.ProjectPropertiesStartupObjectNotSet);
        PressSave(dialog);

        Assert.True(await WaitUntilAsync(() => !File.ReadAllText(project).Contains("StartupObject", StringComparison.Ordinal)),
            "choosing (Not set) did not remove the startup object.");
        Assert.DoesNotContain("StartupObject", File.ReadAllText(project));
    }

    [AvaloniaFact]
    public async Task AValueTheListDoesNotHoldIsShownAndKeptWhenSavingWithoutAChange()
    {
        var project = WriteProject("Acme", startupObject: "Acme.Gone");
        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        var box = StartupObjectBox(dialog);
        Assert.True(await WaitUntilAsync(() => ItemsOf(box).Contains("Acme.Program")), "the startup types were not listed.");

        Assert.Contains("Acme.Gone", ItemsOf(box));
        Assert.Equal("Acme.Gone", box.SelectedItem);

        PressSave(dialog);

        // Saved without a change, the value is written back as it was, not dropped for the first entry of the list.
        Assert.True(await WaitUntilAsync(() => File.ReadAllText(project).Contains("<StartupObject>Acme.Gone</StartupObject>", StringComparison.Ordinal)),
            "the startup object was dropped by saving the page unchanged.");
    }

    /// <summary>Changes the root namespace on the page, then saves; waits until the project file has the new root.</summary>
    private static async Task ChangeRootAndSaveAsync(ProjectPropertiesDialog dialog, string project, string from, string to)
    {
        var root = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == from);
        root.Text = to;

        PressSave(dialog);

        Assert.True(await WaitUntilAsync(() => File.ReadAllText(project).Contains($"<RootNamespace>{to}</RootNamespace>", StringComparison.Ordinal)),
            "saving did not write the new root namespace.");
    }

    [AvaloniaFact]
    public async Task ARootNamespaceChangeLeavesASubMainWrittenAsItIs()
    {
        // "Sub Main" is not a type, so the root namespace is not put in front of it.
        var project = WriteProject("Acme", startupObject: "Sub Main");
        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        var box = StartupObjectBox(dialog);
        Assert.True(await WaitUntilAsync(() => ItemsOf(box).Contains("Acme.Program")), "the startup types were not listed.");

        await ChangeRootAndSaveAsync(dialog, project, "Acme", "Other");

        Assert.Contains("<StartupObject>Sub Main</StartupObject>", File.ReadAllText(project));
    }

    [AvaloniaFact]
    public async Task ARootNamespaceChangeLeavesAValueOutsideTheListAsItIs()
    {
        var project = WriteProject("Acme", startupObject: "Acme.Gone");
        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        var box = StartupObjectBox(dialog);
        Assert.True(await WaitUntilAsync(() => ItemsOf(box).Contains("Acme.Program")), "the startup types were not listed.");

        await ChangeRootAndSaveAsync(dialog, project, "Acme", "Other");

        Assert.Contains("<StartupObject>Acme.Gone</StartupObject>", File.ReadAllText(project));
    }

    // ------------------------------------------------------------------
    // The Application Framework: the forms are offered, and the form goes to Application.myapp.
    // ------------------------------------------------------------------

    /// <summary>
    /// A Windows Forms project with the framework: MyType is WindowsForms, My Project/Application.myapp sets
    /// MySubMain, and StartupObject is the framework's own class. Forms are declared with a stub Form, as in the
    /// other tests; the real build test below uses the real Windows Forms.
    /// </summary>
    private string WriteFrameworkProject(string mainForm, bool withStub)
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "My Project"));

        // The real build needs the Windows Forms reference, which UseWindowsForms gives.
        var windowsForms = withStub ? "" : "\n    <UseWindowsForms>true</UseWindowsForms>";
        var project = Path.Combine(directory, "App.vbproj");
        File.WriteAllText(project, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Acme</RootNamespace>
                <MyType>WindowsForms</MyType>{windowsForms}
                <StartupObject>Acme.My.MyApplication</StartupObject>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(directory, "My Project", "Application.myapp"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <MyApplicationData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <MySubMain>true</MySubMain>
              <MainForm>{mainForm}</MainForm>
              <SingleInstance>false</SingleInstance>
              <ShutdownMode>0</ShutdownMode>
              <EnableVisualStyles>true</EnableVisualStyles>
              <AuthenticationMode>0</AuthenticationMode>
              <ApplicationType>0</ApplicationType>
              <SaveMySettingsOnExit>true</SaveMySettingsOnExit>
            </MyApplicationData>
            """);

        File.WriteAllText(Path.Combine(directory, "Program.vb"), """
            Module Program
                Sub Main()
                End Sub
            End Module
            """);
        if (withStub)
        {
            File.WriteAllText(Path.Combine(directory, "Forms.vb"), """
                Namespace Global.System.Windows.Forms
                    Public Class Form
                    End Class
                End Namespace
                """);
        }

        foreach (var name in new[] { "Form1", "Form2" })
        {
            File.WriteAllText(Path.Combine(directory, name + ".vb"), $"""
                Public Class {name}
                    Inherits System.Windows.Forms.Form
                End Class
                """);
        }

        return project;
    }

    [AvaloniaFact]
    public async Task TheFrameworkOffersTheFormsAndWritesTheChosenFormToItsFileOnly()
    {
        var project = WriteFrameworkProject("Form1", withStub: true);
        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        var box = StartupObjectBox(dialog);
        Assert.True(await WaitUntilAsync(() => ItemsOf(box).Contains("Form2")), "the forms were not listed.");

        // Only the forms are offered: the framework starts the program, not a module with a Main of its own.
        Assert.Equal([L(StringKeys.ProjectPropertiesStartupObjectNotSet), "Form1", "Form2"], ItemsOf(box));
        Assert.Equal("Form1", box.SelectedItem);

        box.SelectedItem = "Form2";
        PressSave(dialog);

        var myApp = Path.Combine(Path.GetDirectoryName(project)!, "My Project", "Application.myapp");
        Assert.True(await WaitUntilAsync(() => File.ReadAllText(myApp).Contains("<MainForm>Form2</MainForm>", StringComparison.Ordinal)),
            "saving did not write the chosen form to Application.myapp.");

        // The startup object stays the framework's class.
        Assert.Contains("<StartupObject>Acme.My.MyApplication</StartupObject>", File.ReadAllText(project));
    }

    [Fact]
    public async Task TheFrameworkProjectWithTheChosenFormBuildsWithTheRealWindowsForms()
    {
        // A real build, once: the framework's generated start-up code must name the chosen form and compile.
        var project = WriteFrameworkProject("Form2", withStub: false);

        var result = await new Basalt.Workspace.MsBuildBuildService().BuildAsync(project);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
    }

    /// <summary>
    /// The Windows Forms project of <c>dotnet new winforms -lang VB</c>: its StartupObject is Sub Main, its module is
    /// Program, and it has Form1. The project sets no RootNamespace, so the names carry the project name, App.
    /// </summary>
    private string WriteTemplateProject(string startupObject)
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var project = Path.Combine(directory, "App.vbproj");
        File.WriteAllText(project, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <StartupObject>{startupObject}</StartupObject>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(directory, "Program.vb"), """
            Module Program
                Sub Main()
                    ApplicationConfiguration.Initialize()
                    Application.Run(New Form1())
                End Sub
            End Module
            """);
        File.WriteAllText(Path.Combine(directory, "Form1.vb"), """
            Public Class Form1
                Inherits System.Windows.Forms.Form
            End Class
            """);

        return project;
    }

    [Fact]
    public async Task TheTemplateProjectOffersItsSubMainModuleAndItsForm()
    {
        var project = WriteTemplateProject("Sub Main");

        var found = await StartupObjectCatalog.ReadAsync(project);

        Assert.Contains("App.Program", found.Names);
        Assert.Contains("App.Form1", found.Forms);
    }

    [AvaloniaTheory]
    [InlineData("Sub Main")]
    [InlineData("Acme.Gone")]
    public async Task SavingUnchangedKeepsAStartupObjectThatTheListDoesNotHold(string startupObject)
    {
        var project = WriteTemplateProject(startupObject);
        var before = await File.ReadAllBytesAsync(project);
        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        // The value is shown, and selected, before the compiler has listed anything.
        var box = StartupObjectBox(dialog);
        Assert.Contains(startupObject, ItemsOf(box));
        Assert.Equal(startupObject, box.SelectedItem);

        // Then the test waits for the list itself, not for a time: the page reports its completion. A failure to read
        // the project is shown in the page's status and fails here with that reason.
        await dialog.StartupObjectsFilled;
        Assert.True(string.IsNullOrEmpty(dialog.FindControl<TextBlock>("StatusLabel")!.Text), dialog.FindControl<TextBlock>("StatusLabel")!.Text);
        Assert.Contains("App.Program", ItemsOf(box));
        Assert.Equal(startupObject, box.SelectedItem);

        PressSave(dialog);
        Assert.True(await WaitUntilAsync(() => !dialog.IsVisible), "the dialog did not close after Save.");

        // Saved without touching the dropdown: the file is exactly as it was.
        Assert.Equal(before, await File.ReadAllBytesAsync(project));
    }

    [AvaloniaFact]
    public async Task AnEditorChangedAndThenRestoredIsWrittenBackAsItWasOpened()
    {
        var project = WriteTemplateProject("Sub Main");
        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        // The assembly name shows the file name, App. It is changed, the change is applied on losing focus, then the
        // opening value is put back and applied the same way.
        var assemblyName = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "App");
        // Losing focus runs the box's commit, which is the same action Save runs for every box. It is run directly here,
        // because a headless window does not move focus between controls.
        static void LoseFocus(TextBox box) => ((Action)box.Tag!).Invoke();

        assemblyName.Text = "Renamed";
        LoseFocus(assemblyName);
        assemblyName.Text = "App";
        LoseFocus(assemblyName);

        PressSave(dialog);
        Assert.True(await WaitUntilAsync(() => !dialog.IsVisible), "the dialog did not close after Save.");

        var xml = await File.ReadAllTextAsync(project);
        Assert.Contains("<AssemblyName>App</AssemblyName>", xml);
        Assert.DoesNotContain("Renamed", xml);
    }

    [AvaloniaFact]
    public async Task TheFrameworksDesignerNamesTheChosenFormAndTheProjectBuildsWithIt()
    {
        var project = WriteFrameworkProject("Form1", withStub: false);
        var designer = Path.Combine(Path.GetDirectoryName(project)!, "My Project", "Application.Designer.vb");
        File.WriteAllText(designer, """
            Namespace My
                Partial Friend Class MyApplication
                    <Global.System.Diagnostics.DebuggerStepThroughAttribute()>
                    Protected Overrides Sub OnCreateMainForm()
                        Me.MainForm = Global.Acme.Form1
                    End Sub
                End Class
            End Namespace
            """);

        var dialog = ProjectPropertiesDialog.For(project);
        dialog.Show();

        var box = StartupObjectBox(dialog);
        Assert.True(await WaitUntilAsync(() => ItemsOf(box).Contains("Form2")), "the forms were not listed.");
        box.SelectedItem = "Form2";
        PressSave(dialog);

        Assert.True(await WaitUntilAsync(() => File.ReadAllText(designer).Contains("Me.MainForm = Global.Acme.Form2", StringComparison.Ordinal)),
            "saving did not write the chosen form into the designer.");
        Assert.DoesNotContain("Global.Acme.Form1", File.ReadAllText(designer));

        // A real build of the project as it is now: the framework's start-up code names the chosen form and compiles.
        var result = await new Basalt.Workspace.MsBuildBuildService().BuildAsync(project);
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
    }
}
