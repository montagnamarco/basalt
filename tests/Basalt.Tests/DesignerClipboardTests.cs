using Avalonia.Headless.XUnit;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Tests;

/// <summary>
/// Cutting, copying and pasting controls in the designer.
/// </summary>
public sealed class DesignerClipboardTests
{
    private static DesignerSession Open() => new(
        XamlDocument.Parse(
            """
            <Window xmlns="https://github.com/avaloniaui">
              <Canvas>
                <Button Canvas.Left="10" Canvas.Top="20" Content="One" />
                <Button Canvas.Left="50" Canvas.Top="60" Content="Two" />
              </Canvas>
            </Window>
            """),
        Basalt.Core.Model.SourceLanguage.VisualBasic);

    private static List<System.Xml.Linq.XElement> Buttons(DesignerSession session) =>
        [.. session.Document.Root.Descendants().Where(e => e.Name.LocalName == "Button")];

    [AvaloniaFact]
    public void NothingToPasteAtFirst()
    {
        Assert.False(Open().CanPaste);
    }

    [AvaloniaFact]
    public void CopyingLeavesTheOriginalAlone()
    {
        var session = Open();

        session.Select(Buttons(session)[0]);
        session.CopySelection();

        Assert.True(session.CanPaste);
        Assert.Equal(2, Buttons(session).Count);
    }

    [AvaloniaFact]
    public void PastingAddsACopyBesideTheOriginal()
    {
        // Pasted exactly on top it looks as though nothing happened, and the
        // copy is then dragged off something the user cannot see.
        var session = Open();

        session.Select(Buttons(session)[0]);
        session.CopySelection();
        session.PasteClipboard();

        var buttons = Buttons(session);

        Assert.Equal(3, buttons.Count);

        var pasted = session.Selection!;

        Assert.Equal("20", pasted.Attribute("Canvas.Left")?.Value);
        Assert.Equal("30", pasted.Attribute("Canvas.Top")?.Value);
    }

    [AvaloniaFact]
    public void ThePastedCopyIsWhatGetsSelected()
    {
        var session = Open();

        session.Select(Buttons(session)[0]);
        session.CopySelection();
        session.PasteClipboard();

        Assert.Same(Buttons(session)[2], session.Selection);
    }

    [AvaloniaFact]
    public void CuttingRemovesTheOriginal()
    {
        var session = Open();

        session.Select(Buttons(session)[0]);
        session.CutSelection();

        Assert.Single(Buttons(session));
        Assert.True(session.CanPaste);
    }

    [AvaloniaFact]
    public void APasteIsOneUndo()
    {
        var session = Open();

        session.SelectMany(Buttons(session));
        session.CopySelection();
        session.PasteClipboard();

        Assert.Equal(4, Buttons(session).Count);

        session.Undo();

        Assert.Equal(2, Buttons(session).Count);
    }

    [AvaloniaFact]
    public void EditingTheOriginalDoesNotChangeWhatIsPasted()
    {
        // Held by value, not by reference: a clipboard that follows later
        // edits pastes something the user never copied.
        var session = Open();

        var first = Buttons(session)[0];

        session.Select(first);
        session.CopySelection();

        session.SetProperty("Content", "Changed");
        session.PasteClipboard();

        Assert.Equal("One", Buttons(session)[2].Attribute("Content")?.Value);
    }
}
