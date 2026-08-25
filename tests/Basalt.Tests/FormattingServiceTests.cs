using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Formatting must handle both languages with their own conventions: braces
/// and four-space indentation for C#, keyword blocks for VB.
/// </summary>
public sealed class FormattingServiceTests : IDisposable
{
    private readonly RoslynFormattingService _service = new();

    [Fact]
    public async Task IndentsVisualBasicBlocks()
    {
        const string messy = """
            Public Class A
            Public Sub M()
            Dim x As Integer=1
            If x>0 Then
            Console.WriteLine(x)
            End If
            End Sub
            End Class
            """;

        var result = await _service.FormatAsync(messy, SourceLanguage.VisualBasic);

        Assert.True(result.Changed);
        Assert.Contains("    Public Sub M()", result.Text);
        Assert.Contains("        Dim x As Integer = 1", result.Text);
        Assert.Contains("        If x > 0 Then", result.Text);
        Assert.Contains("            Console.WriteLine(x)", result.Text);
    }

    [Fact]
    public async Task LeavesUnknownLanguagesUntouched()
    {
        const string source = "not source code at all";

        var result = await _service.FormatAsync(source, SourceLanguage.Unknown);

        Assert.False(result.Changed);
        Assert.Equal(source, result.Text);
    }

    public void Dispose() => _service.Dispose();
}
