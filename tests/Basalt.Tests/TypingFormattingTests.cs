using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Formatting as the user types: pressing Enter indents the new line, and
/// typing a block-closing token pulls its line back to the right level.
///
/// Only the caret's own line may change. Re-indenting the lines above would
/// undo spacing the author put there deliberately, and would move the caret in
/// ways that feel like the editor fighting back.
/// </summary>
public sealed class TypingFormattingTests : IDisposable
{
    private readonly RoslynFormattingService _service = new();

    [Fact]
    public async Task ReindentsAVisualBasicBlockClosingKeyword()
    {
        const string text = """
            Public Class A
                Public Sub M()
                    If x > 0 Then
                        Console.WriteLine(1)
            End If
                End Sub
            End Class
            """;

        var position = text.IndexOf("End If", StringComparison.Ordinal);
        var result = await _service.FormatLineAsync(
            text, SourceLanguage.VisualBasic, position + 3);

        Assert.True(result.Changed);
        Assert.Contains("        End If", result.Text);
    }

    public void Dispose() => _service.Dispose();
}
