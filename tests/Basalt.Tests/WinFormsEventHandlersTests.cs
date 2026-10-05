using Basalt.Workspace;
using static Basalt.Tests.WinFormsCompilationAssert;

namespace Basalt.Tests;

public class WinFormsEventHandlersTests
{
    private const string Designer = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Friend WithEvents Button1 As System.Windows.Forms.Button
        End Class
        """;
    private const string Companion = """
        Partial Class MainForm
            ' Keep unsaved user code and this comment.
            Private Sub Existing(sender As Object, e As System.EventArgs) Handles Button1.Click, Me.Load
                System.Console.WriteLine("Untouched body")
            End Sub
            Private Sub Different()
                System.Console.WriteLine("Different signature")
            End Sub
        End Class
        """;

    [Fact]
    public void CreatesHandlerWithRealEventSignatureAndResolvesNavigationOffset()
    {
        var updated = WinFormsEventHandlers.Set(Companion, "MainForm", "Button1", "MouseDown", "Button1_MouseDown",
            typeof(System.Windows.Forms.MouseEventHandler));
        Assert.Contains("e As Global.System.Windows.Forms.MouseEventArgs", updated);
        Assert.Equal("Button1_MouseDown", WinFormsEventHandlers.Read(updated, "MainForm", "Button1")["MouseDown"]);
        var offset = WinFormsEventHandlers.HandlerOffset(updated, "MainForm", "Button1_MouseDown");
        var line = Microsoft.CodeAnalysis.Text.SourceText.From(updated).Lines.GetLineFromPosition(offset);
        Assert.Equal("        ", line.ToString());
        Assert.Equal(line.End, offset);
        Assert.Contains("' Keep unsaved user code and this comment.", updated);
        Assert.Contains("System.Console.WriteLine(\"Untouched body\")", updated);
        AssertCompiles(Designer, updated);
    }

    [Fact]
    public void ExistingHandlerNavigationTargetsItsFirstStatement()
    {
        var offset = WinFormsEventHandlers.HandlerOffset(Companion, "MainForm", "Existing");
        Assert.StartsWith("System.Console.WriteLine", Companion[offset..]);
    }

    [Fact]
    public void ReassigningAndRemovingBindingsPreservesOtherEventsAndMethodBodies()
    {
        var reassigned = WinFormsEventHandlers.Set(Companion, "MainForm", "Button1", "Click", "Replacement", typeof(EventHandler));
        Assert.Equal("Replacement", WinFormsEventHandlers.Read(reassigned, "MainForm", "Button1")["Click"]);
        Assert.Equal("Existing", WinFormsEventHandlers.Read(reassigned, "MainForm", "Me")["Load"]);
        Assert.Contains("Handles Me.Load", reassigned);
        Assert.Contains("System.Console.WriteLine(\"Untouched body\")", reassigned);
        AssertCompiles(Designer, reassigned);

        var removed = WinFormsEventHandlers.Set(reassigned, "MainForm", "Button1", "Click", null, typeof(EventHandler));
        Assert.False(WinFormsEventHandlers.Read(removed, "MainForm", "Button1").ContainsKey("Click"));
        Assert.Contains("Private Sub Replacement", removed);
        Assert.Equal("Existing", WinFormsEventHandlers.Read(removed, "MainForm", "Me")["Load"]);
        AssertCompiles(Designer, removed);
    }

    [Fact]
    public void ExistingHandlerDoesNotDuplicateBindingsOrRewriteItsBody()
    {
        var same = WinFormsEventHandlers.Set(Companion, "MainForm", "Button1", "Click", "Existing", typeof(EventHandler));
        Assert.Equal(1, same.Split("Button1.Click", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, same.Split("Private Sub Existing", StringSplitOptions.None).Length - 1);
        Assert.Contains("System.Console.WriteLine(\"Untouched body\")", same);
        AssertCompiles(Designer, same);
    }

    [Fact]
    public void ParameterCountMismatchRejectsTheEditWithoutMutatingSource()
    {
        var original = Companion;
        Assert.Throws<NotSupportedException>(() => WinFormsEventHandlers.Set(original, "MainForm", "Button1", "Click", "Different", typeof(EventHandler)));
        Assert.Equal(Companion, original);
        Assert.Equal("Existing", WinFormsEventHandlers.Read(original, "MainForm", "Button1")["Click"]);
    }
}
