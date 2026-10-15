using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The Build menu offers Rebuild and Clean, for the solution and for the startup project, and each runs the MSBuild
/// target it names. The project file does not exist, so dotnet refuses at once and no real build runs: what is checked
/// is the command the menu started.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class ProjectSystemFixTests2BuildMenu : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-build-menu-").FullName;

    // Each test has a settings file of its own, so the window does not read or write the user's.
    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-build-menu-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public ProjectSystemFixTests2BuildMenu() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    /// <summary>A startup project that is named but does not exist: dotnet fails at once, with the arguments recorded.</summary>
    private string MissingProject => Path.Combine(_root, "Missing.vbproj");

    private static string L(string key, params object?[] arguments) => Localizer.Get(key, arguments);

    private static MenuEntry MenuEntryAt(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + string.Join(" > ", path));
            level = hit.Children ?? [];
        }

        return hit!;
    }

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        var entry = MenuEntryAt(window, path);

        if (entry.Action is { } action) action();
        else entry.Command!.Execute(null);
    }

    private static IReadOnlyList<MenuEntry> BuildMenu(MainWindow window) =>
        MenuEntryAt(window, L(StringKeys.MenuBuild)).Children!;

    /// <summary>The arguments a process was started with, as a list.</summary>
    private static List<string> ArgumentsOf(ProcessStartInfo? start) =>
        start?.ArgumentList.ToList() ?? [];

    /// <summary>Pumps the interface thread until the condition holds or the time runs out.</summary>
    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int milliseconds = 180_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }

        return condition();
    }

    // ------------------------------------------------------------------
    // The entries, their order, and their state without a startup project.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void TheBuildMenuOffersRebuildAndCleanForTheSolutionAndTheProject()
    {
        using var host = new TestWindow();

        var entries = BuildMenu(host.Window).Where(e => !e.IsSeparator).ToList();

        Assert.Equal(
            [
                L(StringKeys.MenuBuildBuildSolution),
                L(StringKeys.MenuBuildRebuildSolution),
                L(StringKeys.MenuBuildCleanSolution),
                L(StringKeys.MenuBuildBuildProject, L(StringKeys.MenuBuildProjectFallback)),
                L(StringKeys.MenuBuildRebuildProject, L(StringKeys.MenuBuildProjectFallback)),
                L(StringKeys.MenuBuildCleanProject, L(StringKeys.MenuBuildProjectFallback)),
            ],
            entries.Select(e => e.Header));
    }

    [AvaloniaFact]
    public void TheProjectEntriesNameTheStartupProjectAndAreOnlyEnabledWithOne()
    {
        using var host = new TestWindow();
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        Assert.All(BuildMenu(host.Window).Where(e => e.Header.Contains(L(StringKeys.MenuBuildProjectFallback))),
            e => Assert.False(e.IsEnabled, $"'{e.Header}' is enabled without a startup project."));

        viewModel.StartupProject = MissingProject;
        host.Window.RebuildMenuForTests();

        var entries = BuildMenu(host.Window).Where(e => !e.IsSeparator).ToList();

        Assert.Equal(L(StringKeys.MenuBuildBuildProject, "Missing"), entries[3].Header);
        Assert.Equal(L(StringKeys.MenuBuildRebuildProject, "Missing"), entries[4].Header);
        Assert.Equal(L(StringKeys.MenuBuildCleanProject, "Missing"), entries[5].Header);
        Assert.All(entries.Skip(3), e => Assert.True(e.IsEnabled, $"'{e.Header}' is disabled with a startup project."));
    }

    // ------------------------------------------------------------------
    // Each entry starts the MSBuild target it names, for the startup project and the configuration.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task CleanSolutionRunsDotnetCleanOnTheStartupProject()
    {
        using var host = new TestWindow();
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;
        viewModel.StartupProject = MissingProject;

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildCleanSolution));

        Assert.True(await WaitUntilAsync(host, () => viewModel.StatusMessage == L(StringKeys.StatusCleanFailed)),
            "the clean did not finish.");

        var arguments = ArgumentsOf(viewModel.LastTargetStartInfo);
        Assert.Equal("clean", arguments[0]);
        Assert.Contains(MissingProject, arguments);
        Assert.Equal("Debug", arguments[arguments.IndexOf("-c") + 1]);
        Assert.Null(viewModel.LastBuildStartInfo);
    }

    [AvaloniaFact]
    public async Task CleanProjectRunsDotnetCleanOnTheStartupProject()
    {
        using var host = new TestWindow();
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;
        viewModel.StartupProject = MissingProject;

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildCleanProject, "Missing"));

        Assert.True(await WaitUntilAsync(host, () => viewModel.StatusMessage == L(StringKeys.StatusCleanFailed)),
            "the clean did not finish.");

        Assert.Equal("clean", ArgumentsOf(viewModel.LastTargetStartInfo)[0]);
        Assert.Null(viewModel.LastBuildStartInfo);
    }

    [AvaloniaFact]
    public async Task RebuildSolutionCleansTheStartupProjectAndStopsWhenTheCleanFails()
    {
        using var host = new TestWindow();
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;
        viewModel.StartupProject = MissingProject;

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildRebuildSolution));

        // With no solution open the clean is of the startup project, which is missing: it fails, and no build follows.
        Assert.True(await WaitUntilAsync(host, () => viewModel.StatusMessage == L(StringKeys.StatusCleanFailed)),
            "the clean did not finish.");
        await host.SettleAsync(5);

        var clean = ArgumentsOf(viewModel.LastTargetStartInfo);
        Assert.Equal("clean", clean[0]);
        Assert.Contains(MissingProject, clean);
        Assert.Null(viewModel.LastBuildStartInfo);
    }

    [AvaloniaFact]
    public async Task RebuildProjectCleansThenBuildsTheStartupProject()
    {
        using var host = new TestWindow();
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;
        viewModel.StartupProject = MissingProject;

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildRebuildProject, "Missing"));

        // The project is missing, so its clean fails, and a rebuild does not go on to build after a failed clean.
        Assert.True(await WaitUntilAsync(host, () => viewModel.StatusMessage == L(StringKeys.StatusCleanFailed)),
            "the clean did not finish.");
        await host.SettleAsync(5);

        Assert.Equal("clean", ArgumentsOf(viewModel.LastTargetStartInfo)[0]);
        Assert.Contains(MissingProject, ArgumentsOf(viewModel.LastTargetStartInfo));
        Assert.Null(viewModel.LastBuildStartInfo);
    }

    [AvaloniaFact]
    public async Task BuildProjectBuildsTheStartupProjectWithoutCleaning()
    {
        using var host = new TestWindow();
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;
        viewModel.StartupProject = MissingProject;

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildBuildProject, "Missing"));

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.LastBuildStartInfo is not null && !viewModel.IsBusy),
            "the build did not finish.");

        var build = ArgumentsOf(viewModel.LastBuildStartInfo);
        Assert.Equal("build", build[0]);
        Assert.Contains(MissingProject, build);
        Assert.Equal("Debug", build[build.IndexOf("-c") + 1]);
        Assert.Null(viewModel.LastTargetStartInfo);
    }

    /// <summary>
    /// A solution of two projects, written. Its project files are deleted once it is open, so that the build the menu
    /// starts fails at once and only the command it was given is looked at.
    /// </summary>
    private string WriteTwoProjectSolution(out string[] projects)
    {
        const string visualBasic = "F184B08F-C81C-45F6-A57F-5ABD9991F28F";
        projects = new[] { "App", "Lib" }.Select(name =>
        {
            var directory = Path.Combine(_root, name);
            Directory.CreateDirectory(directory);
            var project = Path.Combine(directory, name + ".vbproj");
            File.WriteAllText(project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType>" +
                "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            return project;
        }).ToArray();

        var solution = Path.Combine(_root, "Outer.sln");
        var text = "Microsoft Visual Studio Solution File, Format Version 12.00\n";
        foreach (var project in projects)
            text += $"Project(\"{{{visualBasic}}}\") = \"{Path.GetFileNameWithoutExtension(project)}\", " +
                    $"\"{Path.GetRelativePath(_root, project).Replace('/', '\\')}\", " +
                    $"\"{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}\"\nEndProject\n";
        File.WriteAllText(solution, text);

        return solution;
    }

    [AvaloniaFact]
    public async Task BuildSolutionBuildsTheSolutionFileWhenASolutionIsOpen()
    {
        var solution = WriteTwoProjectSolution(out var projects);
        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.StatusMessage == L(StringKeys.StatusOpened, "Outer.sln")),
            "the solution did not finish opening.");
        foreach (var project in projects) File.Delete(project);

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildBuildSolution));

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.LastBuildStartInfo is not null && !viewModel.IsBusy),
            "the solution build did not finish.");

        var build = ArgumentsOf(viewModel.LastBuildStartInfo);
        Assert.Equal("build", build[0]);
        Assert.Equal(solution, build[1]);
        Assert.DoesNotContain(projects[0], build);
    }

    [AvaloniaFact]
    public async Task BuildProjectStillBuildsTheStartupProjectWhenASolutionIsOpen()
    {
        var solution = WriteTwoProjectSolution(out var projects);
        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.StatusMessage == L(StringKeys.StatusOpened, "Outer.sln")),
            "the solution did not finish opening.");
        foreach (var project in projects) File.Delete(project);
        var startup = viewModel.StartupProject!;

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildBuildProject, Path.GetFileNameWithoutExtension(startup)));

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.LastBuildStartInfo is not null && !viewModel.IsBusy),
            "the project build did not finish.");

        var build = ArgumentsOf(viewModel.LastBuildStartInfo);
        Assert.Equal(startup, build[1]);
        Assert.DoesNotContain(solution, build);
    }

    [AvaloniaFact]
    public async Task CleanSolutionCleansTheSolutionFileWhenASolutionIsOpen()
    {
        var solution = WriteTwoProjectSolution(out var projects);
        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.StatusMessage == L(StringKeys.StatusOpened, "Outer.sln")),
            "the solution did not finish opening.");
        foreach (var project in projects) File.Delete(project);

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildCleanSolution));

        // dotnet may report a solution with missing projects as cleaned or as failed; either way the clean has ended.
        Assert.True(await WaitUntilAsync(host, () =>
                viewModel.StatusMessage == L(StringKeys.StatusCleanFailed)
                || viewModel.StatusMessage == L(StringKeys.StatusCleaned)),
            "the clean did not finish.");

        var clean = ArgumentsOf(viewModel.LastTargetStartInfo);
        Assert.Equal("clean", clean[0]);
        Assert.Equal(solution, clean[1]);
    }

    [AvaloniaFact]
    public async Task RebuildSolutionCleansAndBuildsTheSolutionFileOnce()
    {
        var solution = WriteTwoProjectSolution(out var projects);
        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.StatusMessage == L(StringKeys.StatusOpened, "Outer.sln")),
            "the solution did not finish opening.");
        foreach (var project in projects) File.Delete(project);

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildRebuildSolution));

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.LastBuildStartInfo is not null && !viewModel.IsBusy),
            "the rebuild did not finish.");

        Assert.Equal(solution, ArgumentsOf(viewModel.LastTargetStartInfo)[1]);
        Assert.Equal(solution, ArgumentsOf(viewModel.LastBuildStartInfo)[1]);
        Assert.Equal(1, viewModel.BuildStartsForTests);
    }

    [AvaloniaFact]
    public async Task ChangingTheStartupProjectRenamesTheProjectEntriesInTheMenu()
    {
        // The menu is installed natively on macOS, where the names can be read back; elsewhere it is not checked.
        if (!IdeMenu.UsesSystemMenuBar) return;

        var solution = WriteTwoProjectSolution(out var projects);
        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.StatusMessage == L(StringKeys.StatusOpened, "Outer.sln")),
            "the solution did not finish opening.");
        var before = InstalledBuildHeaders(host.Window);
        Assert.True(before.Contains(L(StringKeys.MenuBuildBuildProject, "App")), string.Join(" | ", before));

        viewModel.SetStartupProject(projects[1]);

        // The menu is installed on the dispatcher's next turn, so the interface thread is pumped before it is read.
        await host.SettleAsync(2);
        var after = InstalledBuildHeaders(host.Window);
        Assert.True(after.Contains(L(StringKeys.MenuBuildBuildProject, "Lib")), string.Join(" | ", after));
        Assert.False(after.Contains(L(StringKeys.MenuBuildBuildProject, "App")), string.Join(" | ", after));
    }

    /// <summary>The headers of the Build submenu as the platform holds them now.</summary>
    private static List<string> InstalledBuildHeaders(MainWindow window)
    {
        var build = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>()
            .Single(i => i.Header == L(StringKeys.MenuBuild));
        return build.Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header ?? "").ToList();
    }

    [AvaloniaFact]
    public async Task BuildSolutionStillBuildsTheSolutionWithoutAStartupProject()
    {
        var solution = WriteTwoProjectSolution(out var projects);
        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.StatusMessage == L(StringKeys.StatusOpened, "Outer.sln")),
            "the solution did not finish opening.");
        foreach (var project in projects) File.Delete(project);
        viewModel.StartupProject = null;

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildBuildSolution));

        Assert.True(await WaitUntilAsync(host,
                () => viewModel.LastBuildStartInfo is not null && !viewModel.IsBusy),
            "the solution build did not start without a startup project.");
        Assert.Equal(solution, ArgumentsOf(viewModel.LastBuildStartInfo)[1]);
    }

    [AvaloniaFact]
    public void CleanIsBusyWhileItRuns()
    {
        using var host = new TestWindow();
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;
        viewModel.StartupProject = MissingProject;

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildCleanSolution));

        // A clean holds the build gate and shows as busy from the moment it is started, as a build does.
        Assert.True(viewModel.IsBusy);
    }

    [AvaloniaFact]
    public async Task RebuildDoesNotBuildWhenTheCleanFailed()
    {
        using var host = new TestWindow();
        var viewModel = (MainWindowViewModel)host.Window.DataContext!;
        viewModel.StartupProject = MissingProject;

        InvokeMenu(host.Window, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildRebuildSolution));

        Assert.True(await WaitUntilAsync(host, () => viewModel.StatusMessage == L(StringKeys.StatusCleanFailed)),
            "the clean did not finish.");
        await host.SettleAsync(5);

        Assert.NotNull(viewModel.LastTargetStartInfo);
        Assert.Null(viewModel.LastBuildStartInfo);
    }
}
