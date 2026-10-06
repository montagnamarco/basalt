using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Unit tests for the filesystem side of Solution Explorer's Rename and
/// Delete (<see cref="ExplorerFileOperations"/>) — no window, no Avalonia,
/// no Roslyn, just a temporary directory.
/// </summary>
public sealed class ExplorerFileOperationsTests
{
    private static string TempDirectory() =>
        Directory.CreateTempSubdirectory("basalt-explorer-fileops-").FullName;

    [Fact]
    public void RenameMovesAFileToItsNewName()
    {
        var root = TempDirectory();
        var oldPath = Path.Combine(root, "Old.vb");
        File.WriteAllText(oldPath, "Module Old\nEnd Module\n");

        var newPath = ExplorerFileOperations.Rename(oldPath, "New.vb", isDirectory: false);

        Assert.Equal(Path.Combine(root, "New.vb"), newPath);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(newPath));
        Assert.Equal("Module Old\nEnd Module\n", File.ReadAllText(newPath));
    }

    [Fact]
    public void RenameMovesAFolderAndEverythingInsideIt()
    {
        var root = TempDirectory();
        var oldFolder = Path.Combine(root, "OldFolder");
        Directory.CreateDirectory(oldFolder);
        File.WriteAllText(Path.Combine(oldFolder, "Inner.vb"), "Module Inner\nEnd Module\n");

        var newFolder = ExplorerFileOperations.Rename(oldFolder, "NewFolder", isDirectory: true);

        Assert.Equal(Path.Combine(root, "NewFolder"), newFolder);
        Assert.False(Directory.Exists(oldFolder));
        Assert.True(File.Exists(Path.Combine(newFolder, "Inner.vb")));
    }

    /// <summary>
    /// A rename that only changes letter case must still change what the
    /// filesystem reports, on Windows and macOS where a direct move onto a
    /// path that only differs by case is a no-op rather than a rename —
    /// the reason <see cref="ExplorerFileOperations.Rename"/> moves through
    /// a throwaway name first.
    /// </summary>
    [Fact]
    public void RenameWithOnlyACaseChangeStillChangesTheStoredName()
    {
        var root = TempDirectory();
        var oldPath = Path.Combine(root, "lowercase.vb");
        File.WriteAllText(oldPath, "Module LowerCase\nEnd Module\n");

        var newPath = ExplorerFileOperations.Rename(oldPath, "Lowercase.vb", isDirectory: false);

        Assert.Equal(Path.Combine(root, "Lowercase.vb"), newPath);
        Assert.True(File.Exists(newPath));
        Assert.Equal("Module LowerCase\nEnd Module\n", File.ReadAllText(newPath));

        var storedName = Path.GetFileName(Directory.EnumerateFiles(root).Single());
        Assert.Equal("Lowercase.vb", storedName, StringComparer.Ordinal);
    }

    [Fact]
    public void RenameToItsOwnUnchangedNameDoesNothing()
    {
        var root = TempDirectory();
        var path = Path.Combine(root, "Same.vb");
        File.WriteAllText(path, "Module Same\nEnd Module\n");

        var result = ExplorerFileOperations.Rename(path, "Same.vb", isDirectory: false);

        Assert.Equal(path, result);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void RenameRefusesAnAlreadyExistingName()
    {
        var root = TempDirectory();
        var oldPath = Path.Combine(root, "Old.vb");
        File.WriteAllText(oldPath, "Module Old\nEnd Module\n");
        File.WriteAllText(Path.Combine(root, "Taken.vb"), "Module Taken\nEnd Module\n");

        Assert.Throws<IOException>(() => ExplorerFileOperations.Rename(oldPath, "Taken.vb", isDirectory: false));
    }

    [Fact]
    public void DeleteRemovesAFile()
    {
        var root = TempDirectory();
        var path = Path.Combine(root, "Gone.vb");
        File.WriteAllText(path, "Module Gone\nEnd Module\n");

        ExplorerFileOperations.Delete(path, isDirectory: false);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteRemovesAFolderAndEverythingInsideIt()
    {
        var root = TempDirectory();
        var folder = Path.Combine(root, "GoneFolder");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Inner.vb"), "Module Inner\nEnd Module\n");

        ExplorerFileOperations.Delete(folder, isDirectory: true);

        Assert.False(Directory.Exists(folder));
    }
}
