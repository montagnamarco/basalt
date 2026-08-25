using System.Reflection.Metadata;
using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// What a debugger is told about a compiled view.
///
/// The question the pragmas exist to answer: stepping through a rendered
/// page must land in the .vbhtml, and must never stop in generated
/// scaffolding the author cannot open. Read out of a real PDB, because
/// nothing short of that shows what a debugger will actually do.
/// </summary>
public sealed class GeneratedViewSymbolTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"basalt-pdb-{Guid.NewGuid():N}");

    public GeneratedViewSymbolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>Builds a view and reads every sequence point out of the PDB.</summary>
    private async Task<IReadOnlyList<(string Document, int Line, bool Hidden)>>
        SequencePointsAsync(string template)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <DebugType>portable</DebugType>
              </PropertyGroup>
            </Project>
            """);

        var generated = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Page", "App.Views", "Views/Page.vbhtml");

        // Trimmed to what the test is about: without the runtime assembly the
        // view cannot inherit its base class.
        var code = generated.Code
            .Replace($"        Inherits {VbHtmlCodeWriter.BaseTypeName}", "")
            .Replace("Public Overrides Sub Execute()", "Public Sub Execute()")
            .Replace("WriteLiteral(", "Ignore(")
            .Replace("WriteAttribute(", "IgnoreTwo(")
            .Replace("Write(", "Ignore(")
            .Replace("WriteRaw(", "Ignore(")
            .Replace("    End Class",
                "        Private Sub Ignore(value As Object)\n        End Sub\n" +
                "        Private Sub IgnoreTwo(name As String, value As Object)\n" +
                "        End Sub\n    End Class");

        await File.WriteAllTextAsync(Path.Combine(_root, "Generated.vb"), code);

        var build = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                ArgumentList = { "build", _root, "-v", "q", "--nologo" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;

        var output = await build.StandardOutput.ReadToEndAsync();

        await build.WaitForExitAsync();

        Assert.True(build.ExitCode == 0, $"The probe did not build:\n{output}");

        var pdb = Path.Combine(_root, "bin", "Debug", "net10.0", "Probe.pdb");

        using var stream = File.OpenRead(pdb);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);

        var reader = provider.GetMetadataReader();
        var points = new List<(string, int, bool)>();

        foreach (var handle in reader.MethodDebugInformation)
        {
            var info = reader.GetMethodDebugInformation(handle);

            if (info.SequencePointsBlob.IsNil) continue;

            foreach (var point in info.GetSequencePoints())
            {
                var name = point.Document.IsNil
                    ? "?"
                    : Path.GetFileName(
                        reader.GetString(reader.GetDocument(point.Document).Name));

                points.Add((name, point.StartLine, point.IsHidden));
            }
        }

        return points;
    }

    [Fact]
    public async Task EverySymbolNamesTheTemplateAndNotTheGeneratedFile()
    {
        var points = await SequencePointsAsync(
            "@Code\n    Dim a = 1\nEnd Code\n<p>@a</p>");

        Assert.NotEmpty(points);

        // A breakpoint that bound to Generated.vb would open a file the
        // author never wrote and cannot edit.
        Assert.All(points, p => Assert.Equal("Page.vbhtml", p.Document));
    }

    [Fact]
    public async Task TheScaffoldingBetweenRegionsIsHidden()
    {
        var points = await SequencePointsAsync(
            "@Code\n    Dim a = 1\nEnd Code\n<p>@a</p>");

        // 0xFEEFEE: what a compiler emits for a line the debugger must step
        // over. The WriteLiteral calls and the region boundaries get it.
        Assert.Contains(points, p => p.Hidden);
    }

    [Fact]
    public async Task TheLinesThatAreVisibleAreTheOnesTheAuthorWrote()
    {
        // Line 2 holds the statement, line 4 the expression. Line 1 is
        // "@Code" and must not be one of them: an error or a breakpoint on
        // the keyword points at something that is never wrong.
        var visible = (await SequencePointsAsync(
                "@Code\n    Dim a = 1\nEnd Code\n<p>@a</p>"))
            .Where(p => !p.Hidden)
            .Select(p => p.Line)
            .Distinct()
            .OrderBy(line => line)
            .ToList();

        Assert.Equal([2, 4], visible);
    }
}
