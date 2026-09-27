using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Saving a file another program holds open for a moment.
/// </summary>
/// <remarks>
/// Found as a flaky designer test: saving a file written a second earlier
/// failed with "being used by another process", the signature of an
/// antivirus or indexer reading the new file. A user saving hits the same.
/// </remarks>
public sealed class DocumentSaveRetryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-save-retry-").FullName;

    public void Dispose() => ScratchFolder.Delete(_root);

    [Fact]
    public async Task ASaveWaitsForAFileHeldOpenForAMoment()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Sharing violations are a Windows file-system behaviour.");

        var path = Path.Combine(_root, "Held.vb");
        await File.WriteAllTextAsync(path, "old");
        var document = new EditorDocumentViewModel(path, "old") { Text = "new" };

        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150);
            await held.DisposeAsync();
        });

        await document.SaveAsync();
        await release;

        Assert.Equal("new", await File.ReadAllTextAsync(path));
        Assert.False(document.IsModified);
    }

    [Fact]
    public async Task AFileHeldOpenForGoodStillFailsTheSave()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Sharing violations are a Windows file-system behaviour.");

        var path = Path.Combine(_root, "Locked.vb");
        await File.WriteAllTextAsync(path, "old");
        var document = new EditorDocumentViewModel(path, "old") { Text = "new" };

        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => document.SaveAsync());

        Assert.True(document.IsModified);
    }
}
