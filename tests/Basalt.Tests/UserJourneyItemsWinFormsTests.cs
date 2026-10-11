using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Core.Localization;
using static Basalt.Tests.UserJourneyItemsTests;

namespace Basalt.Tests;

/// <summary>
/// Add New Item's Windows Form in a WinForms project, the kind the New
/// Project dialog writes. Built apart from the other item journeys because it
/// builds a WinForms project, which the Windows Desktop collection serialises.
/// </summary>
[Collection(WindowsDesktopCollection.Name)]
public sealed class UserJourneyItemsWinFormsTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-items-forms-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-items-forms-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyItemsWinFormsTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    // ------------------------------------------------------------------
    // Solution Explorer > Add New Item > Windows Form in a WinForms project:
    // the form and its Designer.vb are written beside the project's own
    // forms, the designer opens on the new form, and the project builds
    // with it. The dialog also takes the name typed with its .vb extension.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task AWindowsFormIsWrittenOpensInTheDesignerAndTheProjectBuilds()
    {
        var generated = await SolutionTemplates.CreateAsync(_root, "PortableItems", ProjectTemplate.WinFormsApp);
        var folder = Path.GetDirectoryName(generated.ProjectPath)!;

        using var host = new TestWindow(generated.ProjectPath);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var dialog = await OpenAddNewItemFromExplorerAsync(host, vm.Explorer.Roots.Single());

        Assert.Contains(L(StringKeys.AddItemWindowsForm), OfferedTemplates(dialog));
        ChooseTemplate(dialog, ItemTemplate.WindowsForm);
        TypeName(dialog, "Form1.vb");
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(10);

        var formPath = Path.Combine(folder, "Form1.vb");
        var designerPath = Path.Combine(folder, "Form1.Designer.vb");
        Assert.True(File.Exists(formPath), "the form's code was not written");
        Assert.True(File.Exists(designerPath), "the form's designer file was not written");
        Assert.Contains(Flatten(vm.Explorer.Roots), n => n.Path == designerPath);

        Assert.True(await WaitUntilAsync(host, () => vm.OpenDocuments.Any(d => d.FilePath == designerPath),
            TimeSpan.FromSeconds(10)), "the new form did not open");
        Assert.True(await WaitUntilAsync(host,
            () => w.GetVisualDescendants().OfType<WinFormsDesignSurface>().Any(s => s.RootForm is not null),
            TimeSpan.FromSeconds(10)), "the new form did not open in the designer");

        var build = await new MsBuildBuildService().BuildAsync(generated.ProjectPath);
        Assert.True(build.Succeeded, string.Join("\n", build.Diagnostics.Select(d => d.Message)));
    }
}
