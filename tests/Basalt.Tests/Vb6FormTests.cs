using System.Xml.Linq;
using Basalt.Vb6;

namespace Basalt.Tests;

/// <summary>
/// Visual Basic 6 forms, read and turned into Avalonia markup.
/// </summary>
/// <remarks>
/// A .frm is two documents in one file: nested Begin/End declarations, then
/// the form's own Visual Basic. Both halves are plain text, which is what
/// makes reading one possible without Visual Basic installed — a control from
/// an OCX is named and described here even when the OCX is nowhere to be
/// found.
/// </remarks>
public class Vb6FormTests
{
    private const string Form = """
        VERSION 5.00
        Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"
        Begin VB.Form Form1 
           Caption         =   "Anagrafica"
           ClientHeight    =   3195
           ClientWidth     =   4680
           StartUpPosition =   3  'Windows Default
           Begin VB.Frame fraDati 
              Caption         =   "Dati"
              Left            =   120
              Top             =   120
              Begin VB.TextBox txtNome 
                 Height          =   285
                 Left            =   1320
                 Top             =   480
                 Width           =   2295
              End
           End
           Begin VB.CommandButton cmdOk 
              Caption         =   "OK"
              Left            =   1320
              Top             =   1080
              Width           =   1215
           End
           Begin MSComctlLib.ListView lvwElenco 
              Left            =   120
              Top             =   1800
              BeginProperty ColumnHeader(1) {BDD1F052-858B-11D1-B16A-00C0F0283628} 
                 Text            =   "Nome"
              EndProperty
           End
        End
        Attribute VB_Name = "Form1"
        Option Explicit

        Private Sub cmdOk_Click()
            MsgBox "ciao"
        End Sub
        """;

    private static XElement Convert() =>
        XElement.Parse(FormToAxaml.Convert(FormFile.Parse(Form)));

    [Fact]
    public void ReadsTheControlsAndTheCodeApart()
    {
        var form = FormFile.Parse(Form);

        // The frame and the button at the top; the text box is inside the
        // frame, not beside it.
        Assert.Equal(3, form.Root.Children.Count);
        Assert.Contains("cmdOk_Click", form.Code);

        // And the designer half stays out of the code.
        Assert.DoesNotContain("Begin VB.", form.Code);
    }

    [Fact]
    public void ConvertsTwipsToPixels()
    {
        // 1440 twips to the inch, 96 pixels to the inch: a form written 4680
        // wide is 312 across. Getting this wrong is not visible in a single
        // control — it is visible as a form that is the wrong size.
        var window = Convert();

        Assert.Equal("312", window.Attribute("Width")?.Value);
        Assert.Equal("213", window.Attribute("Height")?.Value);
        Assert.Equal("Anagrafica", window.Attribute("Title")?.Value);
    }

    [Fact]
    public void PositionsEachControlWhereTheFormPutIt()
    {
        var button = Convert().Descendants()
            .First(e => e.Attributes().Any(a => a.Value == "cmdOk"));

        Assert.Equal("Button", button.Name.LocalName);
        Assert.Equal("88", button.Attribute("Canvas.Left")?.Value);
        Assert.Equal("72", button.Attribute("Canvas.Top")?.Value);
        Assert.Equal("OK", button.Attribute("Content")?.Value);
    }

    [Fact]
    public void KeepsWhatWasInsideAFrameInsideIt()
    {
        // A frame holds other controls, and losing that puts a tab page's
        // contents loose on the form.
        var frame = Convert().Descendants()
            .First(e => e.Attributes().Any(a => a.Value == "fraDati"));

        Assert.Contains(frame.Descendants(),
            e => e.Attributes().Any(a => a.Value == "txtNome"));
    }

    [Fact]
    public void StandsInForAControlThisMachineDoesNotHave()
    {
        // The point is that the form opens with the gap visible, so the author
        // knows what has to be replaced rather than finding out when it does
        // not appear.
        var missing = Convert().Descendants()
            .First(e => e.Attributes().Any(a => a.Value == "lvwElenco"));

        Assert.Equal("Border", missing.Name.LocalName);
        Assert.Equal("MSComctlLib.ListView", missing.Attribute("Tag")?.Value);
    }

    [Fact]
    public void DropsTheCommentVisualBasicWritesAfterAValue()
    {
        // "StartUpPosition = 3  'Windows Default" — keeping the comment puts
        // it into the property.
        var form = FormFile.Parse(Form);

        Assert.Equal("3", form.Root.Text("StartUpPosition"));
    }

    [Fact]
    public void NamesTheOcxTheFormAsksFor()
    {
        Assert.Contains("MSCOMCTL.OCX", FormFile.Parse(Form).Objects);
    }
}
