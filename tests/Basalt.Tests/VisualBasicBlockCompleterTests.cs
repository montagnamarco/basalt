using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class VisualBasicBlockCompleterTests
{
    [Theory]
    [InlineData("Dim action = Sub()", "End Sub")]
    [InlineData("Dim factory = Function()", "End Function")]
    [InlineData("Dim factory = Function() As Integer", "End Function")]
    [InlineData("Dim action = Async Sub()", "End Sub")]
    [InlineData("Dim factory = Async Function()", "End Function")]
    [InlineData("Consume(Function()", "End Function")]
    public async Task ClosesAMultilineLambda(string opening, string closing)
    {
        var text = $"Module Program\n    Sub Main()\n        {opening}";

        Assert.Equal(closing, await VisualBasicBlockCompleter.GetClosingFor(text, 2));
    }

    [Theory]
    [InlineData("Dim action = Sub()", "End Sub")]
    [InlineData("Dim factory = Function()", "End Function")]
    public async Task DoesNotDuplicateALambdaTerminator(string opening, string closing)
    {
        var text = $"Module Program\n    Sub Main()\n        {opening}\n        {closing}\n    End Sub\nEnd Module";

        Assert.Null(await VisualBasicBlockCompleter.GetClosingFor(text, 2));
    }

    [Theory]
    [InlineData("Dim action = Sub() Console.WriteLine()")]
    [InlineData("Dim factory = Function() 42")]
    [InlineData("' Dim factory = Function()")]
    [InlineData("Dim text = \"Function()\"")]
    public async Task DoesNotCloseASingleLineLambdaOrText(string statement)
    {
        var text = $"Module Program\n    Sub Main()\n        {statement}\n    End Sub\nEnd Module";

        Assert.Null(await VisualBasicBlockCompleter.GetClosingFor(text, 2));
    }

    [Fact]
    public async Task DoesNotCloseALambdaFromItsBody()
    {
        const string text = "Module Program\n    Sub Main()\n        Dim factory = Function()\n            Return 42";

        Assert.Null(await VisualBasicBlockCompleter.GetClosingFor(text, 3));
    }

    [Fact]
    public async Task ClosesAnUnterminatedRegion()
    {
        Assert.Equal("#End Region", await VisualBasicBlockCompleter.GetClosingFor("#Region \"Helpers\"", 0));
    }

    [Fact]
    public async Task DoesNotDuplicateARegionTerminator()
    {
        Assert.Null(await VisualBasicBlockCompleter.GetClosingFor("#Region \"Helpers\"\n#End Region", 0));
    }

    [Fact]
    public async Task AClosedNestedRegionDoesNotCloseItsParent()
    {
        const string text = "#Region \"Outer\"\n#Region \"Inner\"\n#End Region";

        Assert.Equal("#End Region", await VisualBasicBlockCompleter.GetClosingFor(text, 0));
        Assert.Null(await VisualBasicBlockCompleter.GetClosingFor(text, 1));
    }

    [Fact]
    public async Task RegionTextInsideACommentDoesNotCountAsATerminator()
    {
        const string text = "#Region \"Helpers\"\n' #End Region";

        Assert.Equal("#End Region", await VisualBasicBlockCompleter.GetClosingFor(text, 0));
        Assert.Null(await VisualBasicBlockCompleter.GetClosingFor(text, 1));
    }

    [Theory]
    [InlineData("If True Then", "End If")]
    [InlineData("For index = 0 To 10", "Next")]
    [InlineData("For Each item In items", "Next")]
    [InlineData("Do", "Loop")]
    [InlineData("While True", "End While")]
    [InlineData("Select Case value", "End Select")]
    [InlineData("Try", "End Try")]
    [InlineData("Using resource", "End Using")]
    [InlineData("SyncLock gate", "End SyncLock")]
    [InlineData("With value", "End With")]
    public async Task PreservesOrdinaryBlockCompletion(string opening, string closing)
    {
        var text = $"Module Program\n    Sub Main()\n        {opening}";

        Assert.Equal(closing, await VisualBasicBlockCompleter.GetClosingFor(text, 2));
        Assert.Null(await VisualBasicBlockCompleter.GetClosingFor(text + "\n        " + closing, 2));
    }
}
