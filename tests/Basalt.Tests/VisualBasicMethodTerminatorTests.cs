using Basalt.Core.Model;
using Basalt.Workspace;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Tests;

public sealed class VisualBasicMethodTerminatorTests : IDisposable
{
    private readonly RoslynFormattingService _service = new();

    [Theory]
    [InlineData("Function", "Sub")]
    [InlineData("Sub", "Function")]
    public async Task SynchronizesTheAssociatedMethodTerminator(string declaration, string oldTerminator)
    {
        var text = $"Class C\n    {declaration} M()\n    End {oldTerminator} ' keep this comment\nEnd Class";
        var caret = text.IndexOf("M()", StringComparison.Ordinal) + 3;

        var result = await _service.ApplyTypingConventionsAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text.Replace($"End {oldTerminator}", $"End {declaration}", StringComparison.Ordinal), result.Text);
        Assert.True(result.Changed);
        Assert.Equal(caret, result.Caret);
        var root = await VisualBasicSyntaxTree.ParseText(result.Text).GetRootAsync();
        var block = root
            .DescendantNodes().OfType<MethodBlockSyntax>().Single();
        Assert.False(block.ContainsDiagnostics);
    }

    [Theory]
    [InlineData("Function", "Sub")]
    [InlineData("Sub", "Function")]
    public async Task PreservesNestedLambdaAndUnrelatedMethodTerminators(string declaration, string oldTerminator)
    {
        var text = $"Class C\n    {declaration} M()\n        Dim action = Sub()\n                         Dim literal = \"End {oldTerminator}\"\n                     End Sub\n        Dim factory = Function()\n                          Return 1\n                      End Function\n        ' End {oldTerminator}\n    End {oldTerminator}\n    Sub Other()\n    End Sub\nEnd Class";
        var caret = text.IndexOf("M()", StringComparison.Ordinal) + 3;

        var result = await _service.ApplyTypingConventionsAsync(text, SourceLanguage.VisualBasic, caret);

        var end = text.IndexOf($"\n    End {oldTerminator}\n    Sub Other", StringComparison.Ordinal);
        var expected = text.Remove(end + 9, oldTerminator.Length).Insert(end + 9, declaration);
        Assert.Equal(expected, result.Text);
        Assert.Equal(caret, result.Caret);
    }

    [Theory]
    [InlineData("Class C\n    Function M(\n    End Sub\nEnd Class", "Function M(")]
    [InlineData("Class C\n    Function M()\nEnd Class", "Function M()")]
    [InlineData("Class C\n    Function M()\n        Dim action = Sub()\n    End Sub\nEnd Class", "Function M()")]
    [InlineData("Class C\n    Function M()\n        If True Then\n    End Sub\nEnd Class", "Function M()")]
    [InlineData("Class C\n    Function M()\n    End Sub\n    Sub Other()\n    End Sub\nEnd Class", "Sub Other()")]
    [InlineData("Class C\n    Function M()\n        ' Function M()\n    End Sub\nEnd Class", "' Function M()")]
    [InlineData("Class C\n    Function M()\n        Dim literal = \"End Sub\"\n    End Sub\nEnd Class", "Dim literal = \"End Sub\"")]
    [InlineData("Class C\n    Function M()\n    End Function\nEnd Class", "Function M()")]
    [InlineData("Class C\n    Sub New()\n    End Function\nEnd Class", "Sub New()")]
    public async Task LeavesIncompleteOrUnrelatedTerminatorsAlone(string text, string caretLine)
    {
        var caret = text.IndexOf(caretLine, StringComparison.Ordinal) + caretLine.Length;

        var result = await _service.ApplyTypingConventionsAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text, result.Text);
        Assert.False(result.Changed);
        Assert.Equal(caret, result.Caret);
    }

    [Fact]
    public async Task SynchronizationAndDeclarationFormattingKeepTheCaretOnTheDeclaration()
    {
        const string text = "Class C\n    function M ( )\n    End Sub\nEnd Class";
        var caret = text.IndexOf("M ( )", StringComparison.Ordinal) + 5;

        var result = await _service.ApplyTypingConventionsAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal("Class C\n    Function M()\n    End Function\nEnd Class", result.Text);
        Assert.Equal(caret - 2, result.Caret);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task PreservesLineEndingsAndTypedGenericDeclaration(string newline)
    {
        var text = string.Join(newline,
            "Class C", "    Function [Compute](Of T)(value As T) As T", "        Return value", "    End Sub", "End Class");
        var caret = text.IndexOf(" As T", text.IndexOf("value As T", StringComparison.Ordinal), StringComparison.Ordinal) + 5;

        var result = await _service.ApplyTypingConventionsAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text.Replace("End Sub", "End Function", StringComparison.Ordinal), result.Text);
        Assert.Equal(caret, result.Caret);
    }

    public void Dispose() => _service.Dispose();
}
