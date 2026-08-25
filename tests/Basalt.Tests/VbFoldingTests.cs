using Avalonia.Headless.XUnit;
using AvaloniaEdit.Document;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// Which parts of a Visual Basic file can be folded away.
///
/// Read from the text by line and keyword rather than from Roslyn: folding
/// has to keep working while the file is half-typed, which is exactly when
/// the parse tree is least useful.
/// </summary>
public class VbFoldingTests
{
    private static IReadOnlyList<string> Names(string code)
    {
        var document = new TextDocument(code);

        return [.. VbFoldingStrategy.Foldings(document).Select(f => f.Name ?? "")];
    }

    private static int Count(string code) =>
        VbFoldingStrategy.Foldings(new TextDocument(code)).Count;

    [AvaloniaFact]
    public void FindsAModule()
    {
        var found = Names("""
            Module Program
                Dim x = 1
            End Module
            """);

        Assert.Single(found);
        Assert.Contains("Module Program", found[0], StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void FindsABlockInsideAnother()
    {
        Assert.Equal(2, Count("""
            Class Greeter
                Sub Hello()
                    Dim x = 1
                End Sub
            End Class
            """));
    }

    [AvaloniaFact]
    public void FindsEveryKindOfBlock()
    {
        var code = """
            Namespace App
                Interface IThing
                End Interface
                Structure Point
                End Structure
                Enum Colour
                    Red
                End Enum
                Class Greeter
                    Function Greet() As String
                        Return ""
                    End Function
                End Class
            End Namespace
            """;

        // Namespace, interface, structure, enum, class, function.
        Assert.Equal(6, Count(code));
    }

    [AvaloniaFact]
    public void DoesNotFoldAOneLineProperty()
    {
        // An auto-property has no body to hide.
        Assert.Equal(1, Count("""
            Class Person
                Public Property Name As String
            End Class
            """));
    }

    [AvaloniaFact]
    public void FoldsAPropertyThatHasABody()
    {
        var found = Count("""
            Class Person
                Public Property Name() As String
                    Get
                        Return ""
                    End Get
                End Property
            End Class
            """);

        // The class and the property.
        Assert.Equal(2, found);
    }

    [AvaloniaFact]
    public void FindsARegion()
    {
        var found = Names("""
            #Region "Helpers"
            Sub One()
            End Sub
            #End Region
            """);

        Assert.Contains(found, n => n.Contains("Helpers", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void LeavesAnUnclosedBlockAlone()
    {
        // Half-typed: there is nothing to fold to yet, and inventing an end
        // would hide the rest of the file.
        Assert.Equal(0, Count("Module Program\n    Dim x = 1\n"));
    }

    [AvaloniaFact]
    public void IgnoresAKeywordInsideAComment()
    {
        Assert.Equal(0, Count("' Class Greeter\n' End Class\n"));
    }

    [AvaloniaFact]
    public void IsNotConfusedByEndSub()
    {
        // "End Sub" holds the word Sub and opens nothing.
        Assert.Equal(1, Count("""
            Sub One()
            End Sub
            """));
    }

    [AvaloniaFact]
    public void GivesEachFoldTheLineItHides()
    {
        var found = Names("""
            Public Overrides Sub Render()
                Dim x = 1
            End Sub
            """);

        Assert.Contains("Public Overrides Sub Render()", found[0], StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheFoldsComeInTheOrderTheyStart()
    {
        var document = new TextDocument("""
            Class A
                Sub One()
                End Sub
                Sub Two()
                End Sub
            End Class
            """);

        var folds = VbFoldingStrategy.Foldings(document);

        var starts = folds.Select(f => f.StartOffset).ToList();

        Assert.Equal(starts.OrderBy(o => o).ToList(), starts);
    }

    [AvaloniaFact]
    public void AnEmptyFileFoldsNothing()
    {
        Assert.Equal(0, Count(""));
    }
}
