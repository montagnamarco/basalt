using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Writing a constructor from a type's fields, and a property in front of one.
///
/// Roslyn offers neither for Visual Basic without a diagnostic to hang them
/// on, so these check what is written here instead.
/// </summary>
public sealed class GenerateMemberTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-generate", Guid.NewGuid().ToString("N"));

    public GenerateMemberTests() => Directory.CreateDirectory(_root);

    private async Task<(RoslynLanguageService Service, string Path)> OpenAsync(string code)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        var path = Path.Combine(_root, "Program.vb");
        await File.WriteAllTextAsync(path, code);

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        return (service, path);
    }

    private static int Caret(string code, string fragment)
    {
        var at = code.IndexOf(fragment, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{fragment}' is not in the source.");

        return at;
    }

    private const string WithFields = """
        Public Class Person
            Private _name As String
            Private _age As Integer
        End Class
        """;

    // A constructor from the fields

    [Fact]
    public async Task WritesAConstructorTakingEveryField()
    {
        var (service, path) = await OpenAsync(WithFields);
        using var _ = service;

        var preview = await service.PreviewGenerateConstructorAsync(
            path, Caret(WithFields, "Public Class Person"));

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Public Sub New(name As String, age As Integer)",
            updated, StringComparison.Ordinal);
        Assert.Contains("Me._name = name", updated, StringComparison.Ordinal);
        Assert.Contains("Me._age = age", updated, StringComparison.Ordinal);
        Assert.Contains("End Sub", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutsTheConstructorInsideTheClass()
    {
        var (service, path) = await OpenAsync(WithFields);
        using var _ = service;

        var preview = await service.PreviewGenerateConstructorAsync(
            path, Caret(WithFields, "Public Class Person"));

        var updated = preview.Changes.Single().NewText;

        Assert.True(
            updated.IndexOf("Public Sub New", StringComparison.Ordinal)
                < updated.IndexOf("End Class", StringComparison.Ordinal),
            $"The constructor landed outside the class:\n{updated}");
    }

    [Fact]
    public async Task KeepsTheFieldsInTheOrderTheyAreDeclared()
    {
        // A constructor whose parameters are in a different order from the
        // fields is one whose calls are easy to get wrong.
        var (service, path) = await OpenAsync(WithFields);
        using var _ = service;

        var preview = await service.PreviewGenerateConstructorAsync(
            path, Caret(WithFields, "Public Class Person"));

        var line = preview.Changes.Single().NewText
            .Split('\n')
            .First(l => l.Contains("Public Sub New", StringComparison.Ordinal));

        Assert.True(
            line.IndexOf("name", StringComparison.Ordinal)
                < line.IndexOf("age", StringComparison.Ordinal),
            $"The parameters came out reordered: {line}");
    }

    [Fact]
    public async Task RefusesWhenThereIsAlreadyAConstructor()
    {
        const string code = """
            Public Class Person
                Private _name As String
                Public Sub New()
                End Sub
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewGenerateConstructorAsync(
            path, Caret(code, "Public Class Person"));

        Assert.False(preview.CanApply);
        Assert.Contains("already has a constructor", preview.Problem ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesATypeWithNoFields()
    {
        const string code = """
            Public Class Empty
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewGenerateConstructorAsync(
            path, Caret(code, "Public Class Empty"));

        Assert.False(preview.CanApply);
        Assert.Contains("no fields", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LeavesOutConstantsAndSharedFields()
    {
        // Neither belongs to an instance, so neither is something a
        // constructor takes.
        const string code = """
            Public Class Person
                Private Const Limit As Integer = 10
                Private Shared _count As Integer
                Private _name As String
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewGenerateConstructorAsync(
            path, Caret(code, "Public Class Person"));

        Assert.True(preview.CanApply, preview.Problem);

        var line = preview.Changes.Single().NewText
            .Split('\n')
            .First(l => l.Contains("Public Sub New", StringComparison.Ordinal));

        Assert.Contains("name", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Limit", line, StringComparison.Ordinal);
        Assert.DoesNotContain("count", line, StringComparison.Ordinal);
    }

    // A property in front of a field

    [Fact]
    public async Task WritesAPropertyForTheFieldTheCaretIsOn()
    {
        var (service, path) = await OpenAsync(WithFields);
        using var _ = service;

        var preview = await service.PreviewGeneratePropertyAsync(
            path, Caret(WithFields, "_name"));

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Public Property Name As String", updated, StringComparison.Ordinal);
        Assert.Contains("Return _name", updated, StringComparison.Ordinal);
        Assert.Contains("_name = value", updated, StringComparison.Ordinal);
        Assert.Contains("End Property", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeavesTheFieldWhereItWas()
    {
        var (service, path) = await OpenAsync(WithFields);
        using var _ = service;

        var preview = await service.PreviewGeneratePropertyAsync(
            path, Caret(WithFields, "_name"));

        Assert.Contains("Private _name As String", preview.Changes.Single().NewText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAFieldWhoseNameWouldClashWithItsProperty()
    {
        // A field already named like a property would give two members of one
        // name, which does not compile.
        const string code = """
            Public Class Person
                Private Name As String
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewGeneratePropertyAsync(path, Caret(code, "Name As"));

        Assert.False(preview.CanApply);
        Assert.Contains("same name", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaysSoWhenTheCaretIsNotOnAField()
    {
        var (service, path) = await OpenAsync(WithFields);
        using var _ = service;

        var preview = await service.PreviewGeneratePropertyAsync(
            path, Caret(WithFields, "End Class"));

        Assert.False(preview.CanApply);
        Assert.Contains("Put the caret on a field", preview.Problem ?? "",
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
