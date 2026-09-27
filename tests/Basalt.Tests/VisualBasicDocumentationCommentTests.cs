using Basalt.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using System.Xml.Linq;

namespace Basalt.Tests;

public sealed class VisualBasicDocumentationCommentTests
{
    private static Task<DocumentationCommentInsertion?> GenerateAsync(string markedSource)
    {
        var caret = markedSource.IndexOf('|');
        return VisualBasicDocumentationCommentService.GenerateAsync(markedSource.Remove(caret, 1), caret);
    }

    [Theory]
    [InlineData("Sub Save(value As Integer)", "<param name=\"value\"></param>", false)]
    [InlineData("Function Read(value As Integer) As String", "<param name=\"value\"></param>", true)]
    [InlineData("Sub New(value As Integer)", "<param name=\"value\"></param>", false)]
    [InlineData("Function Map(Of T)(value As T) As T", "<typeparam name=\"T\"></typeparam>", true)]
    public async Task GeneratesMemberDocumentation(string declaration, string expectedTag, bool returns)
    {
        var result = await GenerateAsync("Class C\n    '''|\n    " + declaration + "\n    End " +
            (declaration.StartsWith("Function") ? "Function" : "Sub") + "\nEnd Class");
        Assert.NotNull(result);
        Assert.StartsWith(" <summary>\n    ''' \n    ''' </summary>", result.Text);
        Assert.Contains(expectedTag, result.Text);
        Assert.Contains("<param name=\"value\"></param>", result.Text);
        Assert.Equal(returns, result.Text.Contains("<returns>"));
        Assert.Equal(" <summary>\n    ''' ".Length, result.CaretOffset);
    }

    [Fact]
    public async Task PreservesCrLfAndTabIndentation()
    {
        var result = await GenerateAsync("Class C\r\n\t'''|\r\n\tSub Save()\r\n\tEnd Sub\r\nEnd Class");
        Assert.NotNull(result);
        Assert.Equal(" <summary>\r\n\t''' \r\n\t''' </summary>", result.Text);
    }

    [Fact]
    public async Task SupportsInterfaceMembersAndEscapedParameterNames()
    {
        var result = await GenerateAsync("Interface I\n    '''|\n    Function Read([Class] As Integer) As String\nEnd Interface");
        Assert.NotNull(result);
        Assert.Contains("<param name=\"Class\"></param>", result.Text);
        Assert.Contains("<returns></returns>", result.Text);
    }

    [Fact]
    public async Task SupportsAnAttributedMethod()
    {
        var result = await GenerateAsync("Class C\n    '''|\n    <Obsolete>\n    Sub Save(value As Integer)\n    End Sub\nEnd Class");
        Assert.NotNull(result);
        Assert.Contains("<param name=\"value\"></param>", result.Text);
    }

    [Theory]
    [InlineData("Class C\n    Sub M()\n        '''|\n        Dim x = 1\n    End Sub\nEnd Class")]
    [InlineData("Class C\n    Sub M()\n        '''|\n    End Sub\nEnd Class")]
    [InlineData("Class C\n    Dim x = \"'''|\"\nEnd Class")]
    [InlineData("Class C\n    ' comment '''|\n    Sub M()\n    End Sub\nEnd Class")]
    [InlineData("Class C\n    ''' <summary>Existing</summary>\n    '''|\n    Sub M()\n    End Sub\nEnd Class")]
    [InlineData("Class C\n    '''|\n    ''' <summary>Existing</summary>\n    Sub M()\n    End Sub\nEnd Class")]
    [InlineData("Class C\n    '''| text\n    Sub M()\n    End Sub\nEnd Class")]
    [InlineData("Class C\n    ''''|\n    Sub M()\n    End Sub\nEnd Class")]
    [InlineData("Class C\n    '''|\nEnd Class")]
    [InlineData("Class C\n    Sub M()\n        Dim xml = <root>\n'''|\n</root>\n    End Sub\nEnd Class")]
    [InlineData("Class C\n    '''|\n    Sub M(As Integer)\n    End Sub\nEnd Class")]
    [InlineData("Class C\n    '''|\n    Sub M(value As Integer\n    End Sub\nEnd Class")]
    public async Task RejectsInvalidContextsAndExistingDocumentation(string source)
    {
        Assert.Null(await GenerateAsync(source));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(100)]
    public async Task RejectsPositionsOutsideTheTrigger(int caretPosition)
    {
        Assert.Null(await VisualBasicDocumentationCommentService.GenerateAsync("Class C\nEnd Class", caretPosition));
    }

    [Fact]
    public async Task HonorsCancellationBeforeParsing()
    {
        var cancellation = new CancellationToken(canceled: true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VisualBasicDocumentationCommentService.GenerateAsync("'''", 3, cancellation));
    }

    [Theory]
    [InlineData("Public Property Name As String")]
    [InlineData("Public ReadOnly Property Name As String")]
    [InlineData("Public WriteOnly Property Name As String\n        Set(value As String)\n        End Set\n    End Property")]
    [InlineData("Public Property Name As String\n        Get\n            Return Nothing\n        End Get\n        Set(value As String)\n        End Set\n    End Property")]
    public async Task GeneratesPropertyValueDocumentation(string declaration)
    {
        var result = await GenerateAsync("Class C\n    '''|\n    " + declaration + "\nEnd Class");
        Assert.NotNull(result);
        Assert.Equal(" <summary>\n    ''' \n    ''' </summary>\n    ''' <value></value>", result.Text);
        Assert.Equal(" <summary>\n    ''' ".Length, result.CaretOffset);
    }

    [Fact]
    public async Task GeneratesIndexerParametersAndPropertyValue()
    {
        var source = """
            Interface I
                '''|
                Default Property Item([Class] As Integer, index As String) As String
            End Interface
            """;
        var result = await GenerateAsync(source);
        Assert.NotNull(result);
        Assert.Equal(" <summary>\n    ''' \n    ''' </summary>\n    ''' <param name=\"Class\"></param>\n    ''' <param name=\"index\"></param>\n    ''' <value></value>", result.Text);
        await AssertCompilerDocumentationAsync(source, result, SyntaxKind.PropertyStatement, "summary", "param", "param", "value");
    }

    [Theory]
    [InlineData("Public Event Changed()", " <summary>\n    ''' \n    ''' </summary>")]
    [InlineData("Public Event Changed([Class] As Integer, text As String)", " <summary>\n    ''' \n    ''' </summary>\n    ''' <param name=\"Class\"></param>\n    ''' <param name=\"text\"></param>")]
    [InlineData("Public Event Changed As ChangedHandler", " <summary>\n    ''' \n    ''' </summary>")]
    public async Task GeneratesEventDocumentationWithoutPropertyOrReturnTags(string declaration, string expected)
    {
        var source = "Class C\n    Public Delegate Sub ChangedHandler(value As Integer)\n    '''|\n    " + declaration + "\nEnd Class";
        var result = await GenerateAsync(source);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Text);
        var tags = declaration.Contains("[Class]", StringComparison.Ordinal)
            ? new[] { "summary", "param", "param" } : ["summary"];
        await AssertCompilerDocumentationAsync(source, result, SyntaxKind.EventStatement, tags);
    }

    [Fact]
    public async Task SupportsCustomEventsWithoutDocumentingAccessorParameters()
    {
        var result = await GenerateAsync("""
            Class C
                Public Delegate Sub ChangedHandler(value As Integer)
                '''|
                Public Custom Event Changed As ChangedHandler
                    AddHandler(value As ChangedHandler)
                    End AddHandler
                    RemoveHandler(value As ChangedHandler)
                    End RemoveHandler
                    RaiseEvent(value As Integer)
                    End RaiseEvent
                End Event
            End Class
            """);
        Assert.NotNull(result);
        Assert.Equal(" <summary>\n    ''' \n    ''' </summary>", result.Text);
    }

    [Theory]
    [InlineData("Class C\n    ''' <value>Existing</value>\n    '''|\n    Property Name As String\nEnd Class")]
    [InlineData("Class C\n    '''|\n    ''' <summary>Existing</summary>\n    Event Changed(value As Integer)\nEnd Class")]
    [InlineData("Class C\n    Property Name As String\n        '''|\n        Get\n            Return Nothing\n        End Get\n    End Property\nEnd Class")]
    [InlineData("Class C\n    '''|\n    Property Item(As Integer) As String\nEnd Class")]
    [InlineData("Class C\n    '''|\n    Event Changed(As Integer)\nEnd Class")]
    public async Task RejectsInvalidPropertyAndEventContexts(string source)
    {
        Assert.Null(await GenerateAsync(source));
    }

    private static async Task AssertCompilerDocumentationAsync(
        string markedSource, DocumentationCommentInsertion insertion, SyntaxKind declarationKind, params string[] tags)
    {
        var caret = markedSource.IndexOf('|');
        var source = markedSource.Remove(caret, 1).Insert(caret, insertion.Text);
        var tree = VisualBasicSyntaxTree.ParseText(source,
            new VisualBasicParseOptions(documentationMode: DocumentationMode.Diagnose));
        var compilation = VisualBasicCompilation.Create("Documentation",
            [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var root = await tree.GetRootAsync();
        var declaration = root.DescendantNodes().Single(node => node.IsKind(declarationKind));
        var model = compilation.GetSemanticModel(tree);
        ISymbol? symbol = declaration switch
        {
            PropertyStatementSyntax property => model.GetDeclaredSymbol(property),
            EventStatementSyntax eventDeclaration => model.GetDeclaredSymbol(eventDeclaration),
            _ => null
        };
        Assert.NotNull(symbol);
        var xml = XElement.Parse(symbol.GetDocumentationCommentXml()!);
        Assert.Equal(tags, xml.Elements().Select(element => element.Name.LocalName));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Id.StartsWith("BC423", StringComparison.Ordinal));
    }
}
