using Avalonia.Headless.XUnit;
using Basalt.Core.Settings;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Coming back to a solution and finding it as it was left.
/// </summary>
/// <remarks>
/// An IDE that forgets means finding the same four files again before any
/// work can start. The state is small — which files were open, which was in
/// front — and restoring it is the difference between resuming and starting
/// over.
/// </remarks>
public sealed class SessionRestoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-session", Guid.NewGuid().ToString("N"));

    public SessionRestoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, string text = "Module A\nEnd Module")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>A view model with settings kept in memory, not on the user's disk.</summary>
    private static (MainWindowViewModel Vm, IdeSettings Settings) WithSettings()
    {
        var settings = new IdeSettings();
        var vm = new MainWindowViewModel
        {
            LoadSettings = () => settings,
            SaveSettings = _ => { },
        };

        return (vm, settings);
    }

    [AvaloniaFact]
    public async Task RemembersWhichFilesWereOpenAndWhichWasInFront()
    {
        var (vm, _) = WithSettings();

        await vm.OpenFileAsync(Write("One.vb"));
        await vm.OpenFileAsync(Write("Two.vb"));

        var session = vm.CaptureSession();

        Assert.Equal(2, session.OpenFiles.Count);
        Assert.EndsWith("Two.vb", session.ActiveFile);
    }

    [AvaloniaFact]
    public async Task PutsThemBackWhenTheSolutionIsOpenedAgain()
    {
        var (vm, _) = WithSettings();

        var one = Write("One.vb");
        var two = Write("Two.vb");

        await vm.RestoreSessionAsync(new SolutionSession
        {
            OpenFiles = [one, two],
            ActiveFile = one,
        });

        Assert.Equal(2, vm.OpenDocuments.Count);
        Assert.Equal(one, vm.ActiveDocument?.FilePath);
    }

    [AvaloniaFact]
    public async Task RemembersWhichWereOpenInTheDesigner()
    {
        // A form reopened as markup when it was left as a drawing is the same
        // file, but not what the person was doing.
        var (vm, _) = WithSettings();

        var form = Write("MainWindow.axaml", """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow"><Grid /></Window>
            """);

        await vm.RestoreSessionAsync(new SolutionSession
        {
            OpenFiles = [form],
            InDesigner = [form],
        });

        Assert.True(vm.OpenDocuments.Single().OpenInDesigner);
    }

    [AvaloniaFact]
    public async Task SkipsAFileThatHasGoneSince()
    {
        // Deleted or moved on purpose: a dialog about it on every startup
        // would be the IDE nagging about a decision already made.
        var (vm, _) = WithSettings();

        var kept = Write("One.vb");

        await vm.RestoreSessionAsync(new SolutionSession
        {
            OpenFiles = [kept, Path.Combine(_root, "Gone.vb")],
        });

        Assert.Equal(kept, vm.OpenDocuments.Single().FilePath);
    }

    [AvaloniaFact]
    public async Task ClosesTheOldSolutionsFiles()
    {
        // They belong to a project no longer loaded: their diagnostics are
        // stale and going to a definition lands nowhere.
        var (vm, _) = WithSettings();

        await vm.OpenFileAsync(Write("Old.vb"));

        Assert.NotEmpty(vm.OpenDocuments);

        vm.CloseAllDocuments();

        Assert.Empty(vm.OpenDocuments);
        Assert.Null(vm.ActiveDocument);
    }

    [Fact]
    public void KeepsEachSolutionsStateApartFromTheOthers()
    {
        // Two projects have nothing to do with each other, and one set of
        // open files would be wrong for both.
        var session = new SessionSettings();

        session.Remember(new SolutionSession { SolutionPath = "/a.sln", OpenFiles = ["/a/One.vb"] });
        session.Remember(new SolutionSession { SolutionPath = "/b.sln", OpenFiles = ["/b/Two.vb"] });

        Assert.Equal(["/a/One.vb"], session.For("/a.sln")!.OpenFiles);
        Assert.Equal(["/b/Two.vb"], session.For("/b.sln")!.OpenFiles);
    }

    [Fact]
    public void ReplacesWhatItRememberedRatherThanPilingUp()
    {
        var session = new SessionSettings();

        session.Remember(new SolutionSession { SolutionPath = "/a.sln", OpenFiles = ["/old.vb"] });
        session.Remember(new SolutionSession { SolutionPath = "/a.sln", OpenFiles = ["/new.vb"] });

        Assert.Single(session.Solutions);
        Assert.Equal(["/new.vb"], session.For("/a.sln")!.OpenFiles);
    }

    [Fact]
    public void ForgetsTheOldestOnceThereAreTooMany()
    {
        var session = new SessionSettings();

        for (var i = 0; i <= SessionSettings.Keep; i++)
            session.Remember(new SolutionSession { SolutionPath = $"/s{i}.sln" });

        Assert.Equal(SessionSettings.Keep, session.Solutions.Count);
        Assert.Null(session.For("/s0.sln"));
    }
}

/// <summary>Remembering which folders were unfolded in the solution tree.</summary>
public class ExpandedFolderTests
{
    [Fact]
    public void CarriesTheOpenFoldersThroughTheSession()
    {
        // The tree is a control and the session is not, so the two are
        // bridged by callbacks: this is the part that must not be dropped.
        var vm = new MainWindowViewModel
        {
            LoadSettings = () => new Basalt.Core.Settings.IdeSettings(),
            SaveSettings = _ => { },
            ReadExpandedFolders = () => ["/repo/src", "/repo/src/App"],
        };

        Assert.Equal(["/repo/src", "/repo/src/App"], vm.CaptureSession().ExpandedFolders);
    }

    [AvaloniaFact]
    public async Task OpensThemAgainWhenTheSessionComesBack()
    {
        var opened = new List<string>();

        var vm = new MainWindowViewModel
        {
            LoadSettings = () => new Basalt.Core.Settings.IdeSettings(),
            SaveSettings = _ => { },
            WriteExpandedFolders = folders => opened.AddRange(folders),
        };

        await vm.RestoreSessionAsync(new Basalt.Core.Settings.SolutionSession
        {
            ExpandedFolders = ["/repo/src"],
        });

        Assert.Equal(["/repo/src"], opened);
    }

    [Fact]
    public void AsksForNothingWhenThereIsNoTree()
    {
        // A view model without a window behind it — every test that does not
        // need one — must still be able to capture a session.
        var vm = new MainWindowViewModel
        {
            LoadSettings = () => new Basalt.Core.Settings.IdeSettings(),
            SaveSettings = _ => { },
        };

        Assert.Empty(vm.CaptureSession().ExpandedFolders);
    }
}
