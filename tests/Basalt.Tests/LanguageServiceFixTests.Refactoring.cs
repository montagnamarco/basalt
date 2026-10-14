using Basalt.Core.Localization;
using Basalt.Workspace;
using Basalt.Workspace.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>
/// Extract Method and Rename defects found by an independent review of the refactorings.
/// </summary>
/// <remarks>
/// Each extraction is compiled again afterwards: a method that reads right and does not build is the defect these
/// guard against. The compile uses the SDK's default imports, which is what a Visual Basic project sees.
/// </remarks>
public sealed partial class LanguageServiceFixTests
{
    // ---- Extract Method: what the new method takes and returns --------------

    /// <summary>The preview for the fragment, and the new text when it can be applied.</summary>
    private async Task<(RefactoringPreview Preview, string Updated)> PreviewExtractAsync(
        string code, string fragment, string name = "Helper")
    {
        code = code.ReplaceLineEndings("\n");
        fragment = fragment.ReplaceLineEndings("\n");

        using var service = await OpenAsync(("Program.vb", code));

        var start = code.IndexOf(fragment, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The fragment is not in the source:\n{fragment}");

        var preview = await service.PreviewExtractMethodAsync(PathOf("Program.vb"), start, fragment.Length, name);

        return (preview, preview.CanApply ? preview.Changes.Single().NewText.ReplaceLineEndings("\n") : "");
    }

    /// <summary>The errors the text has when compiled as a Visual Basic library with the SDK's default imports.</summary>
    private static IReadOnlyList<string> CompileErrors(string source)
    {
        var options = new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithGlobalImports(GlobalImport.Parse(
            [
                "System", "System.Collections.Generic", "System.Linq", "System.Threading",
                "System.Threading.Tasks", "Microsoft.VisualBasic",
            ]));

        var compilation = VisualBasicCompilation.Create(
            "Check",
            [VisualBasicSyntaxTree.ParseText(source)],
            FrameworkReferences(),
            options);

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
    }

    /// <summary>The reference assemblies of the running framework.</summary>
    private static IReadOnlyList<MetadataReference> FrameworkReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToList();

    [Fact]
    public async Task ExtractMethodPassesAParameterThatTheSelectionWritesByRef()
    {
        // "n" is written inside and read after, so the write must reach the caller: it is passed ByRef.
        const string code = """
            Module Program
                Sub Main()
                    Show(1)
                End Sub

                Sub Show(n As Integer)
                    n = n + 1
                    Console.WriteLine(n)
                End Sub
            End Module
            """;

        var (preview, updated) = await PreviewExtractAsync(code, "n = n + 1");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("Private Sub Helper(ByRef n As Integer)", updated, StringComparison.Ordinal);
        Assert.Contains("Helper(n)", updated, StringComparison.Ordinal);
        Assert.Empty(CompileErrors(updated));
    }

    [Fact]
    public async Task ExtractMethodFunctionKeepsTheValueWhenTheSelectionEndsWithANewline()
    {
        // The selection runs to the end of its line, newline included: the value still comes back.
        const string code = """
            Module Program
                Sub Main()
                    Dim a = 1
                    Dim total = a + 1
                    Console.WriteLine(total)
                End Sub
            End Module
            """;

        var (preview, updated) = await PreviewExtractAsync(code, "Dim total = a + 1\n");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("Private Function Helper(a As Integer) As Integer", updated, StringComparison.Ordinal);
        Assert.Contains("Dim total = Helper(a)", updated, StringComparison.Ordinal);
        Assert.Empty(CompileErrors(updated));
    }

    [Fact]
    public async Task ExtractMethodMakesAnAsyncFunctionWhenTheSelectionAwaitsAndReturnsAValue()
    {
        // The new method awaits, so it is async and returns the Task of the value; the call awaits it.
        const string code = """
            Module Program
                Async Function Compute(a As Integer) As Task(Of Integer)
                    Dim total = Await Task.FromResult(a + 1)
                    Console.WriteLine(total)
                    Return total
                End Function
            End Module
            """;

        var (preview, updated) = await PreviewExtractAsync(code, "Dim total = Await Task.FromResult(a + 1)");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("Private Async Function Helper(a As Integer) As Task(Of Integer)", updated, StringComparison.Ordinal);
        Assert.Contains("Dim total = Await Helper(a)", updated, StringComparison.Ordinal);
        Assert.Empty(CompileErrors(updated));
    }

    [Fact]
    public async Task ExtractMethodMakesAnAsyncFunctionReturningTaskWhenTheSelectionAwaitsInAnAsyncSub()
    {
        // An Async Sub has no value to return, so the new method is a Task-returning Function, not a Sub.
        const string code = """
            Module Program
                Async Sub Run(a As Integer)
                    Await Task.Delay(a)
                    Console.WriteLine(a)
                End Sub
            End Module
            """;

        var (preview, updated) = await PreviewExtractAsync(code, "Await Task.Delay(a)");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("Private Async Function Helper(a As Integer) As Task", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("Async Sub Helper", updated, StringComparison.Ordinal);
        Assert.Contains("Await Helper(a)", updated, StringComparison.Ordinal);
        Assert.Empty(CompileErrors(updated));
    }

    [Fact]
    public async Task ExtractMethodRefusesAnAwaitingSelectionThatWritesAParameter()
    {
        // "n" would have to be ByRef, and an Async method cannot take a ByRef parameter (BC36927).
        const string code = """
            Module Program
                Async Function Compute(n As Integer) As Task(Of Integer)
                    n = Await Task.FromResult(n + 1)
                    Return n
                End Function
            End Module
            """;

        var (preview, _) = await PreviewExtractAsync(code, "n = Await Task.FromResult(n + 1)");

        Assert.False(preview.CanApply);
        Assert.Equal(Localizer.Get(StringKeys.RefactorExtractMethodAsyncByRef, "n"), preview.Problem);
    }

    [Fact]
    public async Task ExtractMethodRefusesASelectionWhoseLambdaUsesAParameterItWouldPassByRef()
    {
        // "n" would be ByRef in the new method, and a lambda cannot use a ByRef parameter (BC36639).
        const string code = """
            Module Program
                Sub Show(n As Integer)
                    n = n + 1
                    Dim twice = Function() n * 2
                    Console.WriteLine(n + twice())
                End Sub
            End Module
            """;

        var (preview, _) = await PreviewExtractAsync(code, "n = n + 1\n        Dim twice = Function() n * 2");

        Assert.False(preview.CanApply);
        Assert.Equal(Localizer.Get(StringKeys.RefactorExtractMethodLambdaByRef, "n"), preview.Problem);
    }

    [Fact]
    public async Task ExtractMethodRefusesASelectionThatYields()
    {
        // A method lifted out of an iterator would not be one: the yield would have nowhere to go.
        const string code = """
            Module Program
                Iterator Function Numbers() As IEnumerable(Of Integer)
                    Yield 1
                    Yield 2
                End Function
            End Module
            """;

        var (preview, _) = await PreviewExtractAsync(code, "Yield 1");

        Assert.False(preview.CanApply);
        Assert.Equal(Localizer.Get(StringKeys.RefactorExtractMethodYield), preview.Problem);
    }

    [Fact]
    public async Task ExtractMethodDeclaresTheTypeParametersTheSelectionUses()
    {
        // The selection names T, which only the generic enclosing method declares: the new method declares it
        // too, with the constraint the source gives it, and the call passes it explicitly.
        const string code = """
            Module Program
                Sub Show(Of T As IComparable)(item As T)
                    Dim copy As T = item
                    Console.WriteLine(copy)
                End Sub
            End Module
            """;

        var (preview, updated) = await PreviewExtractAsync(code, "Dim copy As T = item");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("Private Function Helper(Of T As IComparable)(item As T) As T", updated, StringComparison.Ordinal);
        Assert.Contains("Dim copy = Helper(Of T)(item)", updated, StringComparison.Ordinal);
        Assert.Empty(CompileErrors(updated));
    }

    [Fact]
    public async Task ExtractMethodPutsTheMethodAfterThePropertyNotInsideItsAccessor()
    {
        // The selection is in a getter. Inserted after End Get it would sit inside the property, which does not compile.
        const string code = """
            Public Class Box
                Private _value As Integer

                Public ReadOnly Property Doubled As Integer
                    Get
                        Dim half = _value * 2
                        Return half
                    End Get
                End Property
            End Class
            """;

        var (preview, updated) = await PreviewExtractAsync(code, "Dim half = _value * 2");

        Assert.True(preview.CanApply, preview.Problem);

        var endOfProperty = updated.IndexOf("End Property", StringComparison.Ordinal);
        var helper = updated.IndexOf("Private Function Helper", StringComparison.Ordinal);

        Assert.True(helper > endOfProperty,
            $"The method landed inside the property it came from:\n{updated}");
        Assert.Empty(CompileErrors(updated));
    }

    // ---- Rename: what a C# project can and cannot reach --------------------

    /// <summary>
    /// Opens a Visual Basic library that a C# project references, in a solution with both. The C# project is not
    /// compiled by Basalt, so a rename in the library has to decide whether that project could use the symbol.
    /// </summary>
    private async Task<RoslynLanguageService> OpenLibraryWithCSharpConsumerAsync(string code)
    {
        var library = Path.Combine(_root, "Library");
        var consumer = Path.Combine(_root, "Consumer");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(consumer);

        await File.WriteAllTextAsync(Path.Combine(library, "Library.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(library, "Library.vb"), code.ReplaceLineEndings("\n"));

        await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Library/Library.vbproj" />
              </ItemGroup>
            </Project>
            """);

        var solution = Path.Combine(_root, "Blocked.sln");
        await File.WriteAllTextAsync(solution, string.Join('\n',
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "Project(\"{F184B08F-C81C-45F6-A57F-5ABD9991F28F}\") = \"Library\", \"Library\\Library.vbproj\", \"{6F1C3E0A-1B2D-4C5E-8F90-A1B2C3D4E5F6}\"",
            "EndProject",
            "Project(\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\") = \"Consumer\", \"Consumer\\Consumer.csproj\", \"{7A2D4F1B-2C3E-4D5F-9A01-B2C3D4E5F601}\"",
            "EndProject",
            ""));

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(solution);

        return service;
    }

    [Fact]
    public async Task RenameAllowsALocalWhenACSharpProjectUsesTheVisualBasicProject()
    {
        // A local is visible only inside its method: no C# project can name it, so the rename is not blocked.
        // It sits in a Public method of a Public class, so the local itself is what keeps it out of reach.
        const string code = """
            Public Class Program
                Public Sub Run()
                    Dim total As Integer = 1
                    Console.WriteLine(total)
                End Sub
            End Class
            """;

        using var service = await OpenLibraryWithCSharpConsumerAsync(code);

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Library", "Library.vb"),
            code.IndexOf("total As", StringComparison.Ordinal) + 1, "sum");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("sum", preview.Changes.Single().NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameRefusesAPublicMemberWhenACSharpProjectUsesTheVisualBasicProject()
    {
        // A public member is what the C# project would call, and Basalt cannot compile it to find those calls.
        const string code = """
            Public Class Counter
                Public Property Total As Integer
            End Class
            """;

        using var service = await OpenLibraryWithCSharpConsumerAsync(code);

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Library", "Library.vb"),
            code.IndexOf("Total As", StringComparison.Ordinal) + 1, "Sum");

        Assert.False(preview.CanApply);
        Assert.Empty(preview.Changes);
        Assert.Equal(Localizer.Get(StringKeys.RenameBlockedByCSharpProject, "Consumer"), preview.Problem);
    }

    [Fact]
    public async Task RenameAllowsAFriendMemberWhenACSharpProjectUsesTheVisualBasicProject()
    {
        // A Friend member is internal to the library: the C# project cannot name it without InternalsVisibleTo.
        const string code = """
            Public Class Counter
                Friend Property Total As Integer
            End Class
            """;

        using var service = await OpenLibraryWithCSharpConsumerAsync(code);

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Library", "Library.vb"),
            code.IndexOf("Total As", StringComparison.Ordinal) + 1, "Sum");

        Assert.True(preview.CanApply, preview.Problem);
    }

    [Fact]
    public async Task RenameRefusesAFriendMemberWhenInternalsVisibleToNamesTheCSharpProject()
    {
        // The library hands its internals to the C# project, which can then name the member: blocked.
        const string code = """
            <Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")>

            Public Class Counter
                Friend Property Total As Integer
            End Class
            """;

        using var service = await OpenLibraryWithCSharpConsumerAsync(code);

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Library", "Library.vb"),
            code.IndexOf("Total As", StringComparison.Ordinal) + 1, "Sum");

        Assert.False(preview.CanApply);
        Assert.Equal(Localizer.Get(StringKeys.RenameBlockedByCSharpProject, "Consumer"), preview.Problem);
    }

    [Fact]
    public async Task RenameRefusesAPrivateProtectedMemberWhenInternalsVisibleToNamesTheCSharpProject()
    {
        // Private Protected is a Friend level: a C# subclass in an assembly the internals are handed to reaches it.
        const string code = """
            <Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")>

            Public Class Counter
                Private Protected Property Total As Integer
            End Class
            """;

        using var service = await OpenLibraryWithCSharpConsumerAsync(code);

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Library", "Library.vb"),
            code.IndexOf("Total As", StringComparison.Ordinal) + 1, "Sum");

        Assert.False(preview.CanApply);
        Assert.Equal(Localizer.Get(StringKeys.RenameBlockedByCSharpProject, "Consumer"), preview.Problem);
    }

    // ---- Rename: sibling names and the errors a rename introduces ----------

    [Fact]
    public async Task RenameRefusesANameThatDiffersOnlyInCaseFromASibling()
    {
        // Visual Basic names are case-insensitive: TOTAL is Total, so the member collides.
        const string code = """
            Public Class Box
                Public Property Total As Integer
                Public Property Other As Integer
            End Class
            """;

        using var service = await OpenAsync(("Box.vb", code));

        var preview = await service.PreviewRenameAsync(
            PathOf("Box.vb"), code.IndexOf("Other As", StringComparison.Ordinal) + 1, "TOTAL");

        Assert.False(preview.CanApply);
        Assert.Contains("already has a member named TOTAL", preview.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameConflictsRefusesAnErrorThatReplacesAnotherWithTheSameId()
    {
        // The renamed code fixes the BC30451 for Alpha and introduces one for Gamma. Both are BC30451, so counting
        // by ID alone sees no new error; the error is the one about Gamma, which the original did not have.
        const string before = """
            Module Program
                Sub Main()
                    System.Console.WriteLine(Alpha)
                End Sub
            End Module
            """;

        const string after = """
            Module Program
                Dim Alpha As Integer = 1

                Sub Main()
                    System.Console.WriteLine(Alpha)
                    System.Console.WriteLine(Gamma)
                End Sub
            End Module
            """;

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Probe", LanguageNames.VisualBasic)
            .WithMetadataReferences(FrameworkReferences());
        var document = project.AddDocument("Program.vb", SourceText.From(before.ReplaceLineEndings("\n")));
        var original = document.Project.Solution;
        var renamed = original.WithDocumentText(document.Id, SourceText.From(after.ReplaceLineEndings("\n")));

        var introduced = await RenameConflicts.IntroducedErrorAsync(original, renamed, "Unused", "Renamed", CancellationToken.None);

        Assert.NotNull(introduced);
        Assert.Contains("Gamma", introduced, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameConflictsKeepsAnErrorThatNamesTheRenamedSymbol()
    {
        // The code already has "'Total' is not a member of 'Customer'". After Customer becomes Client the message
        // names Client, but it is the same error, not one the rename introduced.
        const string before = """
            Class Customer
            End Class

            Module Program
                Sub Main()
                    Dim c As New Customer()
                    c.Total = 1
                End Sub
            End Module
            """;

        var after = before.Replace("Customer", "Client", StringComparison.Ordinal);

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Probe", LanguageNames.VisualBasic)
            .WithMetadataReferences(FrameworkReferences());
        var document = project.AddDocument("Program.vb", SourceText.From(before.ReplaceLineEndings("\n")));
        var original = document.Project.Solution;
        var renamed = original.WithDocumentText(document.Id, SourceText.From(after.ReplaceLineEndings("\n")));

        var introduced = await RenameConflicts.IntroducedErrorAsync(original, renamed, "Customer", "Client", CancellationToken.None);

        Assert.Null(introduced);
    }
}
