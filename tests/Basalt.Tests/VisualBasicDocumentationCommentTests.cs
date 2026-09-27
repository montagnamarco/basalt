using Basalt.Workspace;

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
}
