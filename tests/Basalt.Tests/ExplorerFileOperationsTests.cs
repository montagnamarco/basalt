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

    [Fact]
    public void FormCompanionsFindsTheDesignerAndResxNextToAFormsVbFile()
    {
        var root = TempDirectory();
        var formPath = Path.Combine(root, "Form1.vb");
        var designerPath = Path.Combine(root, "Form1.Designer.vb");
        var resxPath = Path.Combine(root, "Form1.resx");
        File.WriteAllText(formPath, "Public Class Form1\nEnd Class\n");
        File.WriteAllText(designerPath, "Partial Class Form1\nEnd Class\n");
        File.WriteAllText(resxPath, "<root></root>\n");

        var companions = ExplorerFileOperations.FormCompanions(formPath);

        Assert.Equal(2, companions.Count);
        Assert.Contains(designerPath, companions);
        Assert.Contains(resxPath, companions);
    }

    /// <summary>Most .vb files are not a form and have neither companion.</summary>
    [Fact]
    public void FormCompanionsIsEmptyForAPlainVbFile()
    {
        var root = TempDirectory();
        var path = Path.Combine(root, "Module1.vb");
        File.WriteAllText(path, "Module Module1\nEnd Module\n");

        Assert.Empty(ExplorerFileOperations.FormCompanions(path));
    }

    /// <summary>Asking from the generated Designer.vb half never reports itself as its own companion.</summary>
    [Fact]
    public void FormCompanionsIsEmptyWhenAskedFromTheDesignerFileItself()
    {
        var root = TempDirectory();
        var designerPath = Path.Combine(root, "Form1.Designer.vb");
        File.WriteAllText(Path.Combine(root, "Form1.vb"), "Public Class Form1\nEnd Class\n");
        File.WriteAllText(designerPath, "Partial Class Form1\nEnd Class\n");

        Assert.Empty(ExplorerFileOperations.FormCompanions(designerPath));
    }

    /// <summary>
    /// Renaming a form's own .vb file carries its Designer.vb and .resx
    /// along with it under the new name, the way Visual Studio renames all
    /// three together.
    /// </summary>
    [Fact]
    public void RenameFileCarriesTheDesignerAndResxCompanionsAlong()
    {
        var root = TempDirectory();
        var formPath = Path.Combine(root, "Form1.vb");
        var designerPath = Path.Combine(root, "Form1.Designer.vb");
        var resxPath = Path.Combine(root, "Form1.resx");
        File.WriteAllText(formPath, "Public Class Form1\nEnd Class\n");
        File.WriteAllText(designerPath, "Partial Class Form1\nEnd Class\n");
        File.WriteAllText(resxPath, "<root></root>\n");

        var moves = ExplorerFileOperations.RenameFile(formPath, "Renamed.vb");

        var newFormPath = Path.Combine(root, "Renamed.vb");
        var newDesignerPath = Path.Combine(root, "Renamed.Designer.vb");
        var newResxPath = Path.Combine(root, "Renamed.resx");

        Assert.Equal(3, moves.Count);
        Assert.Contains((formPath, newFormPath), moves);
        Assert.Contains((designerPath, newDesignerPath), moves);
        Assert.Contains((resxPath, newResxPath), moves);

        Assert.True(File.Exists(newFormPath));
        Assert.True(File.Exists(newDesignerPath));
        Assert.True(File.Exists(newResxPath));
        Assert.False(File.Exists(formPath));
        Assert.False(File.Exists(designerPath));
        Assert.False(File.Exists(resxPath));
    }

    /// <summary>
    /// When a companion's target name is already taken, the whole rename is
    /// rolled back rather than leaving the form split between its old and
    /// new name — the primary file renamed but a companion stranded under
    /// the old one.
    /// </summary>
    [Fact]
    public void RenameFileRollsBackWhenACompanionsTargetNameIsTaken()
    {
        var root = TempDirectory();
        var formPath = Path.Combine(root, "Form1.vb");
        var designerPath = Path.Combine(root, "Form1.Designer.vb");
        File.WriteAllText(formPath, "Public Class Form1\nEnd Class\n");
        File.WriteAllText(designerPath, "Partial Class Form1\nEnd Class\n");
        // Already occupies the Designer.vb companion's computed target name.
        File.WriteAllText(Path.Combine(root, "Renamed.Designer.vb"), "' in the way\n");

        Assert.Throws<IOException>(() => ExplorerFileOperations.RenameFile(formPath, "Renamed.vb"));

        Assert.True(File.Exists(formPath), "the primary file was left renamed after the rollback");
        Assert.True(File.Exists(designerPath), "the Designer.vb companion was left stranded after the rollback");
        Assert.False(File.Exists(Path.Combine(root, "Renamed.vb")));
    }

    /// <summary>
    /// The pair Visual Studio writes for every form follows the rename, so
    /// the Designer.vb stays nested under its form when the project goes
    /// back to Visual Studio; nothing else in the project file changes.
    /// </summary>
    [Fact]
    public void RenameFileRewritesTheProjectsDependentUponPair()
    {
        var root = TempDirectory();
        var project = Path.Combine(root, "App.vbproj");
        var projectText = """
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- kept as written -->
              <ItemGroup>
                <Compile Update="Form1.Designer.vb">
                  <DependentUpon>Form1.vb</DependentUpon>
                </Compile>
                <EmbeddedResource Update="Form1.resx">
                  <DependentUpon>Form1.vb</DependentUpon>
                </EmbeddedResource>
                <Compile Update="Form10.Designer.vb">
                  <DependentUpon>Form10.vb</DependentUpon>
                </Compile>
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(project, projectText);
        File.WriteAllText(Path.Combine(root, "Form1.vb"), "Public Class Form1\nEnd Class\n");
        File.WriteAllText(Path.Combine(root, "Form1.Designer.vb"), "Partial Class Form1\nEnd Class\n");
        File.WriteAllText(Path.Combine(root, "Form1.resx"), "<root />");

        var moves = ExplorerFileOperations.RenameFile(Path.Combine(root, "Form1.vb"), "MainForm.vb");
        Assert.Equal(project, ExplorerFileOperations.RewriteProjectItems(moves));

        var expected = projectText
            .Replace("Update=\"Form1.Designer.vb\"", "Update=\"MainForm.Designer.vb\"")
            .Replace("Update=\"Form1.resx\"", "Update=\"MainForm.resx\"")
            .Replace("<DependentUpon>Form1.vb</DependentUpon>", "<DependentUpon>MainForm.vb</DependentUpon>");

        Assert.Equal(expected, File.ReadAllText(project));
    }

    /// <summary>A plain .vb file with no companions renames exactly as <see cref="Rename"/> alone would.</summary>
    [Fact]
    public void RenameFileWithNoCompanionsMovesOnlyTheOneFile()
    {
        var root = TempDirectory();
        var path = Path.Combine(root, "Module1.vb");
        File.WriteAllText(path, "Module Module1\nEnd Module\n");

        var moves = ExplorerFileOperations.RenameFile(path, "Renamed.vb");

        var move = Assert.Single(moves);
        Assert.Equal(path, move.OldPath);
        Assert.Equal(Path.Combine(root, "Renamed.vb"), move.NewPath);
    }
}
