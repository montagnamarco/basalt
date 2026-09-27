using Basalt.Razor.Vb.Classic;
using Basalt.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using System.Runtime.Loader;

namespace Basalt.Tests;

public sealed class VbPageSourceMappingTests
{
    internal static readonly MetadataReference[] References =
    [
        .. (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator).Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(VbPage).Assembly.Location)
    ];

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void CodeExpressionsMembersAndImportsRoundTripWithHeadersAndUtf16(string newline)
    {
        var template = string.Join(newline,
            "<h1>header 😀</h1>",
            "<%@ Import Namespace=\"System.Text\" %>",
            "<%!",
            "Private Function Twice(number As Integer) As Integer",
            "  Return number * 2",
            "End Function",
            "%>",
            "<%",
            " Dim count = 3",
            "",
            " Dim value = Twice(count)",
            "%>",
            "<p>😀 <%= value.ToString() %> <%== \"<strong>\" & value & \"</strong>\" %></p>");
        var generated = VbPageWriter.WriteWithMap(VbPageParser.Parse(template), "Page", "Pages", "/test.vbpage");

        foreach (var token in new[] { "System.Text", "Twice(number", "Return number", "End Function",
            "count = 3", "value = Twice", "value.ToString", "\"<strong>\"" })
        {
            var original = template.IndexOf(token, StringComparison.Ordinal);
            var mapped = generated.Map.ToGenerated(original, template, generated.Code);
            Assert.NotNull(mapped);
            Assert.StartsWith(token, generated.Code[mapped.Value..]);
            Assert.Equal(original, generated.Map.ToOriginal(mapped.Value));
            // Resolve a character inside the actual token too: starts alone
            // can pass when indentation or UTF-16 columns are shifted.
            var inside = original + Math.Min(3, token.Length - 1);
            var generatedInside = generated.Map.ToGenerated(inside, template, generated.Code);
            Assert.Equal(mapped.Value + inside - original, generatedInside);
            Assert.Equal(inside, generated.Map.ToOriginal(generatedInside!.Value));
        }
        Assert.Contains("#ExternalSource(\"/test.vbpage\", 4)", generated.Code);
        Assert.Contains("#ExternalSource(\"/test.vbpage\", 9)", generated.Code);
        var blank = template.IndexOf("Dim count = 3", StringComparison.Ordinal) + "Dim count = 3".Length + newline.Length;
        var blankGenerated = generated.Map.ToGenerated(blank, template, generated.Code, Basalt.Razor.Vb.MappingBehavior.Inclusive);
        Assert.NotNull(blankGenerated);
        Assert.Equal(blank, generated.Map.ToOriginal(blankGenerated.Value, Basalt.Razor.Vb.MappingBehavior.Inclusive));
        Assert.Null(generated.Map.ToOriginal(generated.Code.IndexOf("Inherits Global.", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void MultilineExpressionTokensRoundTripWithoutCountingTheirIndentationTwice(string newline)
    {
        var template = string.Join(newline, "<h1>header 😀</h1>", "<%= String.Concat(",
            "    \"hello\",", "    \"world\") %>");
        var generated = VbPageWriter.WriteWithMap(VbPageParser.Parse(template), "Page", "Pages", "/test.vbpage");
        foreach (var token in new[] { "\"hello\"", "\"world\"" })
        {
            var original = template.IndexOf(token, StringComparison.Ordinal);
            var at = generated.Map.ToGenerated(original, template, generated.Code);
            Assert.NotNull(at);
            Assert.StartsWith(token, generated.Code[at.Value..]);
            Assert.Equal(original, generated.Map.ToOriginal(at.Value));
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task MappedWriterStillCompilesAndRendersStatementsExpressionsAndMembers(string newline)
    {
        var template = string.Join(newline,
            "<%@ Import System.Text %>",
            "<%!",
            "Private Function Twice(number As Integer) As Integer",
            "    Return number * 2",
            "End Function",
            "%>",
            "<%",
            "    Dim count = 3",
            "    Dim value = Twice(count)",
            "%>",
            "<p><%= \"<\" & value.ToString() %></p><%== \"<strong>\" & value & \"</strong>\" %>");
        var generated = VbPageWriter.WriteWithMap(VbPageParser.Parse(template), "Page", "Pages", "/test.vbpage");
        var compilation = VisualBasicCompilation.Create("PageMapping" + Guid.NewGuid().ToString("N"),
            [VisualBasicSyntaxTree.ParseText(generated.Code)], References,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithGlobalImports(new[] { GlobalImport.Parse("Microsoft.VisualBasic") }));
        using var assemblyStream = new MemoryStream();
        var emitted = compilation.Emit(assemblyStream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        assemblyStream.Position = 0;
        var context = new AssemblyLoadContext("PageMapping", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(assemblyStream);
            var page = Assert.IsAssignableFrom<VbPage>(Activator.CreateInstance(assembly.GetType("Pages.Page")!));
            using var output = new StringWriter();
            await page.ExecuteAsync(new DefaultHttpContext(), output);
            var rendered = output.ToString();
            Assert.Contains("<p>&lt;6</p><strong>6</strong>", rendered);
        }
        finally
        {
            context.Unload();
        }
    }
}
