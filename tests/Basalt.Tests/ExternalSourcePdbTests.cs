using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>
/// Where the debugger will stop, read from the PDB the compiler writes.
/// </summary>
/// <remarks>
/// The mapping table the editor uses and the #ExternalSource lines the
/// compiler reads are produced together, but only the second decides where a
/// breakpoint binds. A mapping off by one does not crash anything: the
/// breakpoint lands on the next line, or never binds, and nobody finds out
/// until they debug. So the generated code is really compiled, its portable
/// PDB really read, and every line checked against the template.
/// </remarks>
public class ExternalSourcePdbTests
{
    // A Windows path on every platform: backslashes are where the component
    // writer used to go wrong, and the PDB stores the name as a plain string.
    private const string ViewPath = @"C:\Site\Views\Home\Index.vbhtml";
    private const string ComponentPath = @"C:\Site\Components\Pages\Counter.vbrazor";

    private static readonly MetadataReference[] References =
    [
        .. (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(VbHtmlView).Assembly.Location),
    ];

    /// <summary>
    /// One line of each kind the writers map: statements around a blank line,
    /// a block with a continuation and a closing, a loop, an expression, and
    /// members declared in the template with a blank line of their own.
    /// </summary>
    private const string Body = """
        @Code
            Dim first = 1

            Dim afterBlank = 2
        End Code
        @If first = 1 Then
            <p>one</p>
        @ElseIf first = 2 Then
            <p>two</p>
        @End If
        @For Each item In New Integer() {1, 2}
            <li>@item</li>
        @Next
        @Functions
            Public Function Twice(n As Integer) As Integer
                Dim doubled = n * 2

                Return doubled
            End Function
        @End Functions
        """;

    /// <summary>The one-based template line holding some text.</summary>
    private static int LineOf(string template, string text)
    {
        var at = template.IndexOf(text, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{text}' is not in the template");

        return template[..at].Count(c => c == '\n') + 1;
    }

    /// <summary>A checksum as the build supplies it: SHA-256 of the file's bytes.</summary>
    private static string Sha256Of(string template) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(template)));

    /// <summary>Compiles in Debug and reads back the PDB's documents and lines.</summary>
    private static Pdb Compile(string code)
    {
        var compilation = VisualBasicCompilation.Create(
            "Generated",
            [VisualBasicSyntaxTree.ParseText(code)],
            References,
            new VisualBasicCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug)
                // What every Visual Basic project imports without saying so;
                // the generated code uses vbCrLf from it.
                .WithGlobalImports(GlobalImport.Parse("Microsoft.VisualBasic", "System")));

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();

        var emitted = compilation.Emit(
            peStream,
            pdbStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

        Assert.True(emitted.Success, string.Join("\n",
            emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) + "\n\n" + code);

        pdbStream.Position = 0;

        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
        var reader = provider.GetMetadataReader();

        var lines = new List<(string Document, int Line)>();

        foreach (var handle in reader.MethodDebugInformation)
        {
            foreach (var point in reader.GetMethodDebugInformation(handle).GetSequencePoints())
            {
                if (point.IsHidden) continue;

                var document = reader.GetDocument(point.Document);

                lines.Add((reader.GetString(document.Name), point.StartLine));
            }
        }

        var checksums = reader.Documents
            .Select(reader.GetDocument)
            .ToDictionary(
                d => reader.GetString(d.Name),
                d => (Algorithm: reader.GetGuid(d.HashAlgorithm), Hash: reader.GetBlobBytes(d.Hash)));

        return new Pdb(lines, checksums);
    }

    private sealed record Pdb(
        List<(string Document, int Line)> Lines,
        Dictionary<string, (Guid Algorithm, byte[] Hash)> Checksums)
    {
        public IEnumerable<int> LinesIn(string document) =>
            Lines.Where(l => l.Document == document).Select(l => l.Line).Distinct().Order();
    }

    /// <summary>The lines of the template a breakpoint must be able to bind to.</summary>
    private static readonly string[] BodyLines =
    [
        "Dim first = 1",
        "Dim afterBlank = 2",
        "@If first = 1 Then",
        "@ElseIf first = 2 Then",
        "@End If",
        "@item",
        "@Next",
        "Dim doubled = n * 2",
        "Return doubled",
    ];

    /// <summary>
    /// The other continuations and closings: Select with Case and Case Else,
    /// Try with Catch and Finally, Else, and a Next naming its variable.
    /// </summary>
    private const string Branches = """
        @Select Case DateTime.Now.Second
        @Case 0
            <p>zero</p>
        @Case Else
            <p>other</p>
        @End Select
        @Try
            <p>@(7 \ 2)</p>
        @Catch ex As Exception
            <p>@ex.Message</p>
        @Finally
            <p>done</p>
        @End Try
        @If False Then
            <p>no</p>
        @Else
            <p>yes</p>
        @End If
        @For i = 1 To 2
            <p>@i</p>
        @Next i
        """;

    private static readonly string[] BranchLines =
    [
        "@Select Case",
        "@Case 0",
        "@Case Else",
        "@End Select",
        "@Try",
        "@Catch ex",
        "@Finally",
        "@End Try",
        "@Else",
        "@Next i",
    ];

    private static void AssertEveryLineBinds(
        Pdb pdb, string path, string template, string[] expected)
    {
        var bound = pdb.LinesIn(path).ToList();

        foreach (var text in expected)
        {
            var line = LineOf(template, text);

            Assert.True(bound.Contains(line),
                $"No sequence point on line {line} ('{text}') of {path}; bound: {string.Join(", ", bound)}");
        }

        // And nothing on a blank line, which is where a drift of one shows.
        var blank = template.Split('\n')
            .Select((text, index) => (Text: text, Line: index + 1))
            .Where(l => l.Text.Trim().Length == 0)
            .Select(l => l.Line);

        Assert.Empty(bound.Intersect(blank));
    }

    [Fact]
    public void AViewBindsEveryLineWhereItWasWritten()
    {
        var template = Body.ReplaceLineEndings("\n");

        var code = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Index", "Site.Views.Home", ViewPath,
            checksum: Sha256Of(template)).Code;

        AssertEveryLineBinds(Compile(code), ViewPath, template, BodyLines);
    }

    [Fact]
    public void AComponentBindsEveryLineWhereItWasWritten()
    {
        var template = Body.ReplaceLineEndings("\n");

        var code = VbComponentWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Counter", "Site.Components.Pages", ComponentPath,
            checksum: Sha256Of(template)).Code;

        AssertEveryLineBinds(Compile(code), ComponentPath, template, BodyLines);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void AViewBindsEveryContinuationAndClosing(string lineBreak)
    {
        // CRLF too: a template saved on Windows must bind the same lines.
        var template = Branches.ReplaceLineEndings(lineBreak);

        var code = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Index", "Site.Views.Home", ViewPath).Code;

        AssertEveryLineBinds(Compile(code), ViewPath, template, BranchLines);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void AComponentBindsEveryContinuationAndClosing(string lineBreak)
    {
        var template = Branches.ReplaceLineEndings(lineBreak);

        var code = VbComponentWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Counter", "Site.Components.Pages", ComponentPath).Code;

        AssertEveryLineBinds(Compile(code), ComponentPath, template, BranchLines);
    }

    [Fact]
    public void ACaretInAComponentExpressionReachesTheSameToken()
    {
        // The component writer puts an expression in a local of its own,
        // "Dim __v0 = ", and a caret after "Model." used to land inside the
        // local's name: completion was asked about __v0, not about Model.
        const string template = "<p>@Model.Nome</p>\n";

        var generated = VbComponentWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Counter", "Site.Components.Pages", ComponentPath);

        var caret = template.IndexOf("Nome", StringComparison.Ordinal);

        var at = VbHtmlCodeRegions.CaretInGenerated(template, generated.Code, generated.Map, caret);

        Assert.NotNull(at);
        Assert.StartsWith("Nome", generated.Code[at.Value..], StringComparison.Ordinal);
    }

    [Fact]
    public void ThePdbCarriesTheTemplatesChecksum()
    {
        // What lets a debugger tell the template it opens from one edited
        // since the build, instead of stopping on the wrong line of it.
        var template = Body.ReplaceLineEndings("\n");
        var checksum = Sha256Of(template);

        var code = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Index", "Site.Views.Home", ViewPath,
            checksum: checksum).Code;

        var (algorithm, hash) = Compile(code).Checksums[ViewPath];

        Assert.Equal(new Guid("8829d00f-11b8-4213-878b-770e8597ac16"), algorithm);
        Assert.Equal(checksum, Convert.ToHexStringLower(hash));
    }
}
