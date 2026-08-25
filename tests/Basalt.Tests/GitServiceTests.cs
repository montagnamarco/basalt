using Basalt.Core.Services;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>Checks against a git repository created on the fly.</summary>
public sealed class GitServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-git", Guid.NewGuid().ToString("N"));

    private readonly GitSourceControlService _git;

    public GitServiceTests()
    {
        Directory.CreateDirectory(_root);
        Run("init", "-b", "principale");
        Run("config", "user.email", "prova@esempio.it");
        Run("config", "user.name", "Prova");
        _git = new GitSourceControlService(_root);
    }

    private void Run(params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in arguments) info.ArgumentList.Add(a);
        System.Diagnostics.Process.Start(info)!.WaitForExit();
    }

    [Fact]
    public async Task RiconosceUnRepositoryGit()
    {
        Assert.True(await _git.IsRepositoryAsync(_root));
        Assert.False(await _git.IsRepositoryAsync(Path.GetTempPath()));
    }

    [Fact]
    public async Task LeggeIlRamoCorrente()
    {
        Assert.Equal("principale", await _git.GetCurrentBranchAsync());
    }

    [Fact]
    public async Task LeggeIlRamoAncheSuUnRepositorySenzaCommit()
    {
        // Right after "git init" the branch exists but HEAD points at no
        // commit: the IDE must still show its name.
        Assert.Equal("principale", await _git.GetCurrentBranchAsync());
        Assert.Empty(await _git.GetLogAsync(10));
    }

    [Fact]
    public async Task ElencaIFileNonTracciati()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "nuovo.vb"), "Public Class A\nEnd Class");

        var status = await _git.GetStatusAsync();

        Assert.Contains(status, c => c.Path == "nuovo.vb" && c.Kind == FileChangeKind.Untracked);
    }

    [Fact]
    public async Task DistingueLeModificheInStageDaQuelleNonInStage()
    {
        var file = Path.Combine(_root, "Programma.vb");
        await File.WriteAllTextAsync(file, "versione 1");
        await _git.StageAsync(["Programma.vb"]);

        var status = await _git.GetStatusAsync();

        Assert.Contains(status, c => c.Path == "Programma.vb" && c.Staged && c.Kind == FileChangeKind.Added);
    }

    [Fact]
    public async Task CreaUnCommitELoRitrovaNelLog()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Modulo.vb"), "Module M\nEnd Module");
        await _git.StageAsync(["Modulo.vb"]);
        await _git.CommitAsync("Aggiunge il modulo");

        var log = await _git.GetLogAsync(10);

        var commit = Assert.Single(log);
        Assert.Equal("Aggiunge il modulo", commit.Message);
        Assert.Equal("Prova", commit.Author);
    }

    [Fact]
    public async Task GestisceINomiDiFileConSpazi()
    {
        // The -z format exists precisely for this case.
        await File.WriteAllTextAsync(Path.Combine(_root, "file con spazi.vb"), "x");

        var status = await _git.GetStatusAsync();

        Assert.Contains(status, c => c.Path == "file con spazi.vb");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* irrelevant */ }
    }
}
