using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// What the editor holds reaches Roslyn without reaching the disk.
/// </summary>
/// <remarks>
/// The editors send their text to the language service at every pause in
/// typing. Applied to MSBuildWorkspace that text was written to the file, so
/// an edit the user never saved was on disk half a second later and could not
/// be discarded by closing without saving.
/// </remarks>
public sealed class UnsavedTextTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-unsaved", Guid.NewGuid().ToString("N"));

    public UnsavedTextTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task EditorTextIsAnalysedButNeverWrittenToTheFile()
    {
        var project = Path.Combine(_root, "P.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        var file = Path.Combine(_root, "M.vb");
        var saved = "Module M\n    Sub Main()\n    End Sub\nEnd Module\n";
        File.WriteAllText(file, saved);

        using var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(project);

        Assert.DoesNotContain(await service.GetDiagnosticsAsync(file), d => d.Id == "BC30451");

        // An unsaved edit that uses a name nobody declared.
        await service.UpdateDocumentAsync(file, saved.Replace("    End Sub", "        Undeclared()\n    End Sub"));

        // Roslyn answers from the edit...
        Assert.Contains(await service.GetDiagnosticsAsync(file), d => d.Id == "BC30451");
        // ...and the file still holds what was saved.
        Assert.Equal(saved, File.ReadAllText(file));
    }

    [Fact]
    public async Task EditsToSeveralFilesAccumulateAndAreForgottenWhenTheSolutionIsOpenedAgain()
    {
        var project = Path.Combine(_root, "P.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        var caller = Path.Combine(_root, "Caller.vb");
        var callee = Path.Combine(_root, "Callee.vb");
        var savedCaller = "Module Caller\n    Sub Main()\n    End Sub\nEnd Module\n";
        var savedCallee = "Module Callee\nEnd Module\n";
        File.WriteAllText(caller, savedCaller);
        File.WriteAllText(callee, savedCallee);

        using var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(project);

        // One unsaved edit declares what another unsaved edit calls: the
        // second must be answered against the first, not against the disk.
        await service.UpdateDocumentAsync(callee, "Module Callee\n    Sub Helper()\n    End Sub\nEnd Module\n");
        await service.UpdateDocumentAsync(caller, savedCaller.Replace("    End Sub", "        Helper()\n    End Sub"));

        Assert.DoesNotContain(await service.GetDiagnosticsAsync(caller), d => d.Id == "BC30451");

        // Opening the solution again starts from what is on disk.
        await service.OpenSolutionAsync(project);
        await service.UpdateDocumentAsync(caller, savedCaller.Replace("    End Sub", "        Helper()\n    End Sub"));

        Assert.Contains(await service.GetDiagnosticsAsync(caller), d => d.Id == "BC30451");
    }
}
