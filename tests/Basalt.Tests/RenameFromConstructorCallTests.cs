using Basalt.Workspace.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>
/// Rename started on a type's name where it calls a constructor ("New Person")
/// renames the type, as Visual Studio does; elsewhere a constructor stays what
/// is found.
/// </summary>
/// <remarks>
/// The name in "New Person" binds to Person's implicit constructor, and F2
/// there refused with "'.ctor' cannot be renamed". Reintroduced once (the
/// constructor kept): the three type-renaming cases failed.
/// </remarks>
public sealed class RenameFromConstructorCallTests
{
    private const string Path = "/tmp/Program.vb";

    private const string Source = """
        Module Program
            Sub Main()
                Dim p As New Person
                Dim boxes As New Box(Of Person)
                Dim tagged As New Tagged("x")
            End Sub
        End Module

        Public Class Person
        End Class

        Public Class Box(Of T)
        End Class

        Public Class Tagged
            Public Sub New(name As String)
            End Sub
        End Class

        <System.Obsolete>
        Public Class Old
        End Class
        """;

    private static Solution Solution()
    {
        var workspace = new AdhocWorkspace();
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(System.IO.Path.PathSeparator);
        var project = workspace.AddProject("Rename", LanguageNames.VisualBasic)
            .WithCompilationOptions(new VisualBasicCompilationOptions(OutputKind.ConsoleApplication))
            .AddMetadataReferences(paths.Select(path => MetadataReference.CreateFromFile(path)));
        return project.AddDocument("Program.vb", SourceText.From(Source), filePath: Path).Project.Solution;
    }

    private static async Task<RefactoringPreview> RenameAtAsync(string marker, int offsetInMarker, string newName) =>
        await new RenameRefactoring(Solution()).PreviewAsync(
            Path, Source.IndexOf(marker, StringComparison.Ordinal) + offsetInMarker, newName);

    private static string Renamed(RefactoringPreview preview) => Assert.Single(preview.Changes).NewText;

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public async Task OnTheTypeNameAfterNewTheTypeIsRenamed(int offset)
    {
        // 3 is inside the name; 6 is just after it, where the caret sits after typing it.
        var preview = await RenameAtAsync("Person\n", offset, "Customer");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("Dim p As New Customer", Renamed(preview), StringComparison.Ordinal);
        Assert.Contains("Public Class Customer", Renamed(preview), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnAGenericTypesNameTheGenericTypeIsRenamed()
    {
        var preview = await RenameAtAsync("Box(Of", 1, "Crate");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("New Crate(Of Person)", Renamed(preview), StringComparison.Ordinal);
        Assert.Contains("Public Class Crate(Of T)", Renamed(preview), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnAnExplicitConstructorsCallTheTypeIsRenamed()
    {
        var preview = await RenameAtAsync("Tagged(\"x\")", 2, "Labelled");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("New Labelled(\"x\")", Renamed(preview), StringComparison.Ordinal);
        Assert.Contains("Public Class Labelled", Renamed(preview), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnAnAttributeFromAnotherAssemblyTheRenameIsStillRefused()
    {
        var preview = await RenameAtAsync("Obsolete>", 2, "Ancient");

        Assert.False(preview.CanApply);
    }
}
