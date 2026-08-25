using AvaloniaEdit.Document;
using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>Reading the identifier the pointer rests on.</summary>
public class DebugHoverEvaluatorTests
{
    private static string? WordAt(string text, int offset) =>
        DebugHoverEvaluator.WordAt(new TextDocument(text), offset);

    [AvaloniaFact]
    public void ReadsTheIdentifierUnderThePointer()
    {
        Assert.Equal("total", WordAt("Dim total As Integer", 6));
    }

    [AvaloniaFact]
    public void ReadsADottedNameWhole()
    {
        // Resting on "Name" should evaluate the property, not a variable of
        // that name which does not exist.
        Assert.Equal("customer.Name", WordAt("Console.Write(customer.Name)", 24));
    }

    [AvaloniaFact]
    public void FindsNothingInWhitespace()
    {
        Assert.Null(WordAt("Dim  total", 4));
    }

    [AvaloniaFact]
    public void IgnoresANumber()
    {
        // Evaluating "42" would tell the user nothing they cannot already see.
        Assert.Null(WordAt("total = 42", 8));
    }

    [AvaloniaFact]
    public void CopesWithAnOffsetPastTheEnd()
    {
        Assert.Null(WordAt("total", 99));
    }

    [AvaloniaFact]
    public void CopesWithAnEmptyDocument()
    {
        Assert.Null(WordAt("", 0));
    }

    [AvaloniaFact]
    public void ReadsAnIdentifierAtTheVeryStart()
    {
        Assert.Equal("total", WordAt("total = 1", 0));
    }
}
