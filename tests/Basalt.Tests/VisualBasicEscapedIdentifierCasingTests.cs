using Basalt.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Tests;

public sealed class VisualBasicEscapedIdentifierCasingTests
{
    private static Document CreateDocument(AdhocWorkspace workspace, string source)
    {
        var project = workspace.AddProject("Casing", LanguageNames.VisualBasic)
            .WithCompilationOptions(new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        Assert.True(workspace.TryApplyChanges(project.Solution));
        return workspace.AddDocument(project.Id, "Casing.vb", SourceText.From(source));
    }

    [Theory]
    [InlineData("[vAlUe]", "[Value]")]
    [InlineData("vAlUe", "Value")]
    public async Task CorrectsFieldReferencesPreservingEscaping(string reference, string expected)
    {
        var source = $$"""
            Class C
                Private [Value] As Integer
                Function Read() As Integer
                    Return {{reference}}
                End Function
            End Class
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var changes = await VisualBasicCaseCorrector.GetIdentifierChangesAsync(document);
        Assert.Single(changes);
        Assert.Equal(source.Replace("Return " + reference, "Return " + expected), SourceText.From(source).WithChanges(changes).ToString());
        Assert.All(changes, change => Assert.Equal(change.Span.Length, change.NewText!.Length));

        var root = (await document.GetSyntaxRootAsync())!;
        var name = root.DescendantNodes().OfType<IdentifierNameSyntax>().Single(node => node.Identifier.ValueText == "vAlUe");
        var model = (await document.GetSemanticModelAsync())!;
        Assert.Equal("Value", model.GetSymbolInfo(name).Symbol?.Name);
        var correctedDocument = document.WithText(SourceText.From(source).WithChanges(changes));
        var correctedRoot = (await correctedDocument.GetSyntaxRootAsync())!;
        var correctedName = correctedRoot.DescendantNodes().OfType<IdentifierNameSyntax>().Single(node => node.Identifier.ValueText == "Value");
        var correctedModel = (await correctedDocument.GetSemanticModelAsync())!;
        Assert.Equal(model.GetSymbolInfo(name).Symbol!.ToDisplayString(), correctedModel.GetSymbolInfo(correctedName).Symbol?.ToDisplayString());
    }

    [Theory]
    [InlineData("mAp", "Map", "Map")]
    [InlineData("[sElEcT]", "[Select]", "[Select]")]
    public async Task CorrectsGenericMethodReferences(string reference, string expected, string declaration)
    {
        var source = $$"""
            Class C
                Shared Function {{declaration}}(Of T)(value As T) As T
                    Return value
                End Function
                Function Read() As Integer
                    Return {{reference}}(Of Integer)(1)
                End Function
            End Class
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var changes = await VisualBasicCaseCorrector.GetIdentifierChangesAsync(document);
        Assert.Single(changes);
        Assert.Equal(source.Replace("Return " + reference, "Return " + expected), SourceText.From(source).WithChanges(changes).ToString());
        Assert.Equal(reference.Length, changes[0].Span.Length);
        Assert.Equal(reference.Length, changes[0].NewText!.Length);
        var model = (await document.GetSemanticModelAsync())!;
        var root = (await document.GetSyntaxRootAsync())!;
        var symbol = model.GetSymbolInfo(root.DescendantNodes().OfType<GenericNameSyntax>().Single()).Symbol;
        Assert.Equal(declaration.Trim('[', ']'), symbol?.Name);
        var correctedDocument = document.WithText(SourceText.From(source).WithChanges(changes));
        var correctedRoot = (await correctedDocument.GetSyntaxRootAsync())!;
        var correctedModel = (await correctedDocument.GetSemanticModelAsync())!;
        Assert.Equal(symbol!.ToDisplayString(), correctedModel.GetSymbolInfo(correctedRoot.DescendantNodes().OfType<GenericNameSyntax>().Single()).Symbol?.ToDisplayString());
    }

    [Fact]
    public async Task CorrectsGenericTypesUsingOnlyTheIdentifierToken()
    {
        const string source = """
            Class Container(Of T)
            End Class
            Class C
                Private field As cOnTaInEr(Of Integer)
            End Class
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var changes = await VisualBasicCaseCorrector.GetIdentifierChangesAsync(document);
        Assert.Single(changes);
        Assert.Equal("Container", changes[0].NewText);
        Assert.Equal("cOnTaInEr".Length, changes[0].Span.Length);
        var typeArgument = source.IndexOf("Integer", StringComparison.Ordinal);
        Assert.Empty(await VisualBasicCaseCorrector.GetIdentifierChangesAsync(document, new TextSpan(typeArgument, 7)));
    }

    [Fact]
    public async Task LimitsCorrectionsToTheRequestedReferenceAndPreservesOtherText()
    {
        const string source = """
            Class C
                Private Value As Integer
                Function Read() As Integer
                    Dim text = "vAlUe" ' vAlUe
                    Return [vAlUe] + vAlUe
                End Function
            End Class
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var position = source.LastIndexOf("vAlUe", StringComparison.Ordinal);
        var changes = await VisualBasicCaseCorrector.GetIdentifierChangesAsync(document, new TextSpan(position, 5));
        Assert.Single(changes);
        Assert.Equal("Value", changes[0].NewText);
        Assert.Equal(source[..position] + "Value" + source[(position + 5)..], SourceText.From(source).WithChanges(changes).ToString());
    }

    [Fact]
    public async Task LeavesDeclarationsStringsAndCommentsUnchangedAcrossTheDocument()
    {
        const string source = """
            Class C
                Private Value As Integer
                Sub Read()
                    Dim text = "vAlUe"
                    ' [vAlUe] and vAlUe
                End Sub
            End Class
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        Assert.Empty(await VisualBasicCaseCorrector.GetIdentifierChangesAsync(document));
    }

    [Fact]
    public async Task LeavesUnresolvedAndAmbiguousReferencesUnchanged()
    {
        const string source = """
            Class C
                Shared Function Choose(x As String) As Integer
                    Return 1
                End Function
                Shared Function Choose(x As Integer()) As Integer
                    Return 2
                End Function
                Function Read() As Integer
                    Return cHoOsE(Nothing) + mIsSiNg
                End Function
            End Class
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var root = (await document.GetSyntaxRootAsync())!;
        var model = (await document.GetSemanticModelAsync())!;
        var ambiguous = root.DescendantNodes().OfType<IdentifierNameSyntax>().Single(node => node.Identifier.ValueText == "cHoOsE");
        Assert.Null(model.GetSymbolInfo(ambiguous).Symbol);
        Assert.NotEmpty(model.GetSymbolInfo(ambiguous).CandidateSymbols);
        Assert.Empty(await VisualBasicCaseCorrector.GetIdentifierChangesAsync(document));
    }
}
