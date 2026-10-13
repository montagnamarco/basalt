using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The handler a double-click writes reads as Visual Studio writes it.
/// </summary>
/// <remarks>
/// Double-clicking a form wrote "sender As Global.System.Object": correct, but
/// not the code a Visual Basic developer reads or writes. A type Visual Basic
/// has a keyword for is now written with the keyword; other types keep their
/// Global.-qualified name, which no project import can shadow. Reintroduced
/// once (every type qualified): both tests failed.
/// </remarks>
public sealed class WinFormsEventHandlerSignatureTests
{
    private const string Code = "Public Partial Class MainForm\n    Public Sub New()\n        InitializeComponent()\n    End Sub\nEnd Class\n";

    [Fact]
    public void ALoadHandlerTakesSenderAsObject()
    {
        var written = WinFormsEventHandlers.Set(Code, "MainForm", "Me", "Load", "MainForm_Load", typeof(EventHandler));

        Assert.Contains("Private Sub MainForm_Load(sender As Object, e As Global.System.EventArgs) Handles Me.Load", written, StringComparison.Ordinal);
    }

    private delegate void Progress(object sender, int percent, ref bool cancel, string[] names);

    [Fact]
    public void KeywordTypesAreWrittenAsKeywordsInEveryShape()
    {
        var written = WinFormsEventHandlers.Set(Code, "MainForm", "Worker", "Moved", "Worker_Moved", typeof(Progress));

        Assert.Contains("(sender As Object, percent As Integer, ByRef cancel As Boolean, names As String())", written, StringComparison.Ordinal);
    }
}
