using Avalonia.Threading;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// End-to-end user journeys through a real <see cref="MainWindow"/>, driving
/// the Git panels and the AI Assistant panel the way a person at the IDE
/// would: open a solution that lives inside a real git repository (made with
/// a real <c>git init</c>, in a temp folder, with no remote), edit and save a
/// file, stage it, commit it, look at its history and its diff, make a
/// branch, and open the assistant.
///
/// Every test works against a real git executable in a throwaway repository;
/// none call a real AI provider or touch <c>ApiKeyStore</c>.
/// </summary>
public sealed class UserJourneyGitTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-git-journey-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void RunGit(string arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // Only this repository's own settings: a machine's global signing key or hooks path would
        // otherwise make a commit here prompt, hang or fail.
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_root, ".no-global-gitconfig");
        foreach (var part in arguments.Split(' ')) info.ArgumentList.Add(part);

        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {arguments} failed: {process.StandardError.ReadToEnd()}");
    }

    /// <summary>
    /// A real git repository, with a local identity (so a commit never
    /// depends on whatever is configured on the machine running the test),
    /// no remote, and one commit already in it: a console project just
    /// written and nothing in Git Changes yet.
    /// </summary>
    private string MakeRepoWithConsoleProject()
    {
        RunGit("init -q");
        RunGit("config user.email test@example.com");
        RunGit("config user.name Basalt-Test");

        var project = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"),
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        RunGit("add -A");
        RunGit("commit -q -m Initial-commit");

        return project;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Assert.True(condition());
    }

    // ------------------------------------------------------------------
    // Journey 1: opening the solution shows the repo; editing and saving a
    // file lists it in Git Changes.
    //
    // Regression: MainWindowViewModel.SaveAsync/SaveAllAsync wrote the file
    // and told Roslyn about it, but nothing told the git panels. A user who
    // edited and saved a file, then opened Git Changes to commit it, found
    // an empty list until some unrelated git command happened to refresh it.
    // ------------------------------------------------------------------
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task OpeningTheSolutionShowsTheRepoAndSavingAFileListsItInGitChanges()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;

        // The repo already there when the solution opened is detected
        // without the user asking for anything.
        await WaitUntilAsync(() => vm.Repository is not null);

        var programPath = Path.Combine(_root, "Program.vb");
        await vm.OpenFileAsync(programPath);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text =
            "Module Program\n    Sub Main()\n        Console.WriteLine(\"Hi\")\n    End Sub\nEnd Module\n";
        await host.SettleAsync(5);
        Assert.True(vm.ActiveDocument!.IsModified);

        await vm.SaveAllAsync();

        await WaitUntilAsync(() => host.Window.GitChangesForTests!.Unstaged
            .Any(r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A file saved by something other than Save or Save All - the designer writing a form's code
    /// when an event is wired, a QuickBASIC run saving first - shows in Git Changes all the same.
    /// </summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task AFileSavedOutsideTheSaveCommandsStillListsInGitChanges()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await WaitUntilAsync(() => vm.Repository is not null);

        var programPath = Path.Combine(_root, "Program.vb");
        await vm.OpenFileAsync(programPath);
        await host.SettleAsync(5);

        var document = vm.ActiveDocument!;
        document.Text = "Module Program\n    Sub Main()\n        Console.WriteLine(\"Saved\")\n    End Sub\nEnd Module\n";
        await document.SaveAsync();

        await WaitUntilAsync(() => host.Window.GitChangesForTests!.Unstaged
            .Any(r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal)));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ADocumentClosedWithItsSolutionNoLongerReportsSaves()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(Path.Combine(_root, "Program.vb"));
        await host.SettleAsync(3);
        var closed = vm.ActiveDocument!;
        var reported = 0;
        vm.DocumentSaved += (_, _) => reported++;

        // What opening another solution does with the documents of this one.
        vm.OpenDocuments.Clear();
        await closed.SaveAsync();

        Assert.Equal(0, reported);
    }
}
