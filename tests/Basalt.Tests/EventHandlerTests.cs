using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Tests;

/// <summary>
/// Wiring a control on the form to code that runs.
/// </summary>
/// <remarks>
/// The gesture Visual Basic was built around: double-click a button and you
/// are in the method that runs when it is pressed. Without it the designer
/// draws forms that cannot do anything.
/// </remarks>
public sealed class EventHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-events", Guid.NewGuid().ToString("N"));

    public EventHandlerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DesignerSession SessionFor(string inner, SourceLanguage language = SourceLanguage.VisualBasic)
    {
        var path = Path.Combine(_root, "MainWindow.axaml");

        var xaml = $"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow">
              {inner}
            </Window>
            """;

        File.WriteAllText(path, xaml);

        return new DesignerSession(XamlDocument.Parse(xaml, path), language);
    }

    [Fact]
    public void WritesTheAttributeAndTheMethodTogether()
    {
        // An attribute naming a method that does not exist is a window that
        // will not load: the pair has to be written as one.
        var session = SessionFor("""<Grid><Button x:Name="OkButton" /></Grid>""");

        session.Select(session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Button"));

        var handler = session.AttachHandler();

        Assert.NotNull(handler);
        Assert.Equal("OkButton_Click", handler!.MethodName);
        Assert.Contains("Click=\"OkButton_Click\"", session.Document.ToXaml());
        Assert.Contains("Private Sub OkButton_Click(", handler.Code);
    }

    [Fact]
    public void NamesAControlThatHasNoName()
    {
        // A handler refers to its control by name, so an unnamed control
        // cannot have one: it is given a name rather than refused.
        var session = SessionFor("<Grid><Button /></Grid>");

        session.Select(session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Button"));

        var handler = session.AttachHandler();

        Assert.Equal("Button1_Click", handler!.MethodName);
        Assert.Contains("x:Name=\"Button1\"", session.Document.ToXaml());
    }

    [Fact]
    public void DoesNotReuseANameAlreadyTaken()
    {
        var session = SessionFor("""<Grid><Button x:Name="Button1" /><Button /></Grid>""");

        session.Select(session.Document.Root.Descendants()
            .Last(e => e.Name.LocalName == "Button"));

        Assert.Equal("Button2_Click", session.AttachHandler()!.MethodName);
    }

    [Fact]
    public void GoesBackToTheHandlerThatAlreadyExists()
    {
        // Double-clicking a button a second time is how a person returns to
        // code they wrote; a second copy would break the build.
        var session = SessionFor("""<Grid><Button x:Name="OkButton" /></Grid>""");

        session.Select(session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Button"));

        var first = session.AttachHandler()!;
        File.WriteAllText(first.FilePath, first.Code);

        var again = session.AttachHandler()!;

        Assert.False(again.WasCreated);
        Assert.Equal(1, Occurrences(again.Code, "Sub OkButton_Click("));
    }

    [Fact]
    public void KeepsAHandlerThePersonPointedSomewhereElse()
    {
        // Overwriting it would lose whatever they wired it to.
        var session = SessionFor(
            """<Grid><Button x:Name="OkButton" Click="SharedHandler" /></Grid>""");

        session.Select(session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Button"));

        Assert.Equal("SharedHandler", session.AttachHandler()!.MethodName);
    }

    [Fact]
    public void PicksTheEventAPersonMeansForEachControl()
    {
        // A button means Click and a text box means TextChanged; offering
        // Click on a text box would be a gesture that does nothing useful.
        Assert.Equal("Click", EventHandlers.DefaultEventFor("Button"));
        Assert.Equal("TextChanged", EventHandlers.DefaultEventFor("TextBox"));
        Assert.Equal("SelectionChanged", EventHandlers.DefaultEventFor("ComboBox"));
        Assert.Equal("IsCheckedChanged", EventHandlers.DefaultEventFor("CheckBox"));
    }

    [Fact]
    public void PutsTheMethodInsideTheClass()
    {
        // Before the last End Class, or it lands outside and will not compile.
        var session = SessionFor("""<Grid><Button x:Name="OkButton" /></Grid>""");

        session.Select(session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Button"));

        var code = session.AttachHandler()!.Code;

        var method = code.IndexOf("Sub OkButton_Click(", StringComparison.Ordinal);
        var end = code.LastIndexOf("End Class", StringComparison.Ordinal);

        Assert.True(method > 0 && method < end, "the handler must sit inside the class");
    }

    [Fact]
    public void UndoesTheWiringInOneStep()
    {
        // Naming the control and adding the attribute is one action to the
        // user, so it is one press of undo.
        var session = SessionFor("<Grid><Button /></Grid>");

        session.Select(session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Button"));

        var before = session.Document.ToXaml();

        session.AttachHandler();
        session.Undo();

        Assert.Equal(before, session.Document.ToXaml());
    }

    private static int Occurrences(string text, string what)
    {
        var count = 0;

        for (var i = text.IndexOf(what, StringComparison.Ordinal);
             i >= 0;
             i = text.IndexOf(what, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
