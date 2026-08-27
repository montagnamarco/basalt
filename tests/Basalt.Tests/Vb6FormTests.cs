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

    private const string Project = """
        Type=Exe
        Form=Form1.frm
        Module=Modulo1; Modulo1.bas
        Class=Cliente; Cliente.cls
        Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX
        Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#stdole2.tlb#OLE Automation
        Startup="Form1"
        Name="Anagrafica"
        MajorVer=1
        """;

    [Fact]
    public void ReadsWhatAProjectIsMadeOf()
    {
        var project = ProjectFile.Parse(Project);

        Assert.Equal("Anagrafica", project.Name);
        Assert.Equal("Form1", project.Startup);

        // "Modulo1; Modulo1.bas" — Visual Basic keeps the module's own name
        // beside the path, and taking the whole value gives a file that is
        // not there.
        Assert.Equal(["Modulo1.bas"], project.Modules);
        Assert.Equal(["Cliente.cls"], project.Classes);
        Assert.Equal(["Form1.frm"], project.Forms);
    }

    [Fact]
    public void NamesTheOcxAProjectNeeds()
    {
        // Reported rather than dropped: the project builds without them and
        // the forms show placeholders, so the author is told what to replace
        // instead of finding out when a control does not appear.
        Assert.Equal(["MSCOMCTL.OCX"], ProjectFile.Parse(Project).Objects);
    }

    [Fact]
    public void WritesAProjectBesideTheOneItRead()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "Anagrafica.vbp"), Project);
            File.WriteAllText(Path.Combine(directory, "Form1.frm"), Form);
            File.WriteAllText(Path.Combine(directory, "Modulo1.bas"), "Option Explicit");

            var result = ProjectConversion.Convert(
                Path.Combine(directory, "Anagrafica.vbp"));

            var markup = File.ReadAllText(result.ProjectPath);

            // As AdditionalFiles, not Compile: the .frm is read by a generator
            // during the build and left exactly as Visual Basic 6 wrote it.
            Assert.Contains("<AdditionalFiles Include=\"Form1.frm\" />", markup);
            Assert.DoesNotContain("<Compile Include=\"Form1.frm\"", markup);

            // Option Strict had no equivalent in Visual Basic 6, and a project
            // written against it assigns freely between types: left on,
            // nothing would compile at all.
            Assert.Contains("<OptionStrict>Off</OptionStrict>", markup);
            Assert.Contains("Anagrafica.Form1", markup);

            // Cliente.cls is listed by the project and not on disk, so it is
            // left out rather than named in a project that will not restore.
            Assert.DoesNotContain("Cliente.cls", markup);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadsTheSampleVisualBasic6ProjectAsWritten()
    {
        // Against the file Visual Basic 6 would have written, not against a
        // string shaped for the test: CRLF endings, a comment after a value,
        // a frame with controls inside it and a control from an OCX. Every one
        // of those broke something while this was being built.
        var directory = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "samples", "Basalt.Sample.Vb6");

        if (!Directory.Exists(directory))
        {
            Assert.Skip("the sample project is not beside the tests");
            return;
        }

        var project = ProjectFile.Parse(
            File.ReadAllText(Path.Combine(directory, "Anagrafica.vbp")));

        Assert.Equal("Anagrafica", project.Name);
        Assert.Equal(["Form1.frm"], project.Forms);
        Assert.Equal(["Modulo1.bas"], project.Modules);
        Assert.Equal(["MSCOMCTL.OCX"], project.Objects);

        var form = FormFile.Parse(File.ReadAllText(Path.Combine(directory, "Form1.frm")));

        // The frame, the button and the OCX control at the top; the text box
        // and the label are inside the frame.
        Assert.Equal(3, form.Root.Children.Count);
        Assert.Contains("cmdOk_Click", form.Code);

        var markup = XElement.Parse(FormToAxaml.Convert(form));

        // The one that cannot be brought over is named and outlined, and the
        // window opens with the gap visible.
        Assert.Contains(markup.Descendants(),
            e => e.Attribute("Tag")?.Value == "MSComctlLib.ListView");
    }

    [Fact]
    public void WiresAnEventHandlerTheWayVisualBasicNetNeeds()
    {
        // The one that matters. Visual Basic 6 wired an event by the name of
        // the procedure — cmdOk_Click *was* the handler — and Visual Basic .NET
        // wants it said out loud. Without the clause the code compiles, runs,
        // and does nothing: no error, and the button simply never responds.
        var translated = CodeTranslation.Translate(
            "Private Sub cmdOk_Click()\nEnd Sub", ["cmdOk"]).Code;

        Assert.Contains("Handles cmdOk.Click", translated);
    }

    [Fact]
    public void HandlesTheFormsOwnEventsOnMe()
    {
        // Form_Load has no control called Form to hang from.
        var translated = CodeTranslation.Translate(
            "Private Sub Form_Load()\nEnd Sub", ["txtNome"]).Code;

        Assert.Contains("Handles Me.Load", translated);
    }

    [Fact]
    public void LeavesAMethodThatMerelyLooksLikeAHandlerAlone()
    {
        // A Sub named like a handler for something the form does not have is
        // an ordinary method, and a Handles clause naming a missing control
        // stops the file compiling.
        var translated = CodeTranslation.Translate(
            "Private Sub Report_Print()\nEnd Sub", ["cmdOk"]).Code;

        Assert.DoesNotContain("Handles", translated);
    }

    [Theory]
    [InlineData("s = Trim$(x)", "s = Trim(x)")]
    [InlineData("MsgBox \"ciao\"", "MsgBox(\"ciao\")")]
    [InlineData("MsgBox \"x\", vbCritical", "MsgBox(\"x\", vbCritical)")]
    [InlineData("Set a = Nothing", "a = Nothing")]
    [InlineData("Dim v As Variant", "Dim v As Object")]
    public void RewritesTheSpellingsTheLanguageDropped(string vb6, string expected)
    {
        Assert.Equal(expected, CodeTranslation.Translate(vb6).Code);
    }

    [Fact]
    public void WidensIntegerAndLongToWhatTheyMeantInVisualBasic6()
    {
        // Integer was 16 bits and Long was 32. Keeping the names halves the
        // range of every Integer in the program, silently — a loop that ran to
        // 40000 stops working and nothing says why.
        var translated = CodeTranslation.Translate(
            "Dim n As Integer\nDim big As Long").Code;

        Assert.Contains("n As Short", translated);
        Assert.Contains("big As Integer", translated);
    }

    [Fact]
    public void LeavesAStringLiteralExactlyAsWritten()
    {
        // Rewriting inside a message changes what the program says to whoever
        // is reading the screen.
        const string line = "  Debug.Print(\"use Trim$ here\")";

        Assert.Equal(line, CodeTranslation.Translate(line).Code);
    }

    [Fact]
    public void ReportsWhatItCannotTranslateRatherThanGuessing()
    {
        // A translator that quietly guesses produces code that compiles and
        // behaves differently, which is worse than either of the alternatives.
        var result = CodeTranslation.Translate(
            "    GoSub Etichetta\n    Dim s As String * 10");

        Assert.Equal(2, result.Notes.Count);
        Assert.Contains(result.Notes, n => n.Construct == "GoSub");
        Assert.Contains(result.Notes, n => n.Construct == "fixed-length string");

        // And leaves them alone: a line reported is a line the author reads.
        Assert.Contains("GoSub Etichetta", result.Code);
    }

    [Fact]
    public void WritesAFormAsAClassThatRuns()
    {
        // Where the three halves meet: controls from the designer block,
        // handlers from the code, both against the runtime that sits on
        // Avalonia.
        var written = FormToVisualBasic.Write(FormFile.Parse(Form), "Form1", "/x/Form1.frm");

        Assert.Contains("Inherits Global.Basalt.Vb6.Runtime.Vb6Form", written.Code);

        // WithEvents on every control, because a Handles clause cannot hang
        // from a field without it.
        Assert.Contains("Private WithEvents cmdOk As New", written.Code);
        Assert.Contains("Handles cmdOk.Click", written.Code);

        // A control inside a frame goes on the frame. On the form instead, a
        // group's contents end up loose.
        Assert.Contains("fraDati.Add(txtNome)", written.Code);
        Assert.Contains("Add(cmdOk)", written.Code);

        // And the .frm is named, so an error is reported against it and a
        // breakpoint set there is hit.
        Assert.Contains("#ExternalSource(\"/x/Form1.frm\"", written.Code);
    }

    [Fact]
    public void NamesTheControlsItCouldNotBringOver()
    {
        // In the code as well as on the form: the author is told what to
        // replace rather than finding out when a control does not appear.
        var written = FormToVisualBasic.Write(FormFile.Parse(Form), "Form1");

        Assert.Contains("lvwElenco (MSComctlLib.ListView)", written.Missing);
        Assert.DoesNotContain("WithEvents lvwElenco", written.Code);
    }

    [Fact]
    public void DropsTheOptionStatementsTheFormCarried()
    {
        // The generated file declares its own in the only place Visual Basic
        // .NET accepts them. A second Option Explicit further down is a
        // compile error, not a duplicate — measured, on the sample.
        var written = FormToVisualBasic.Write(FormFile.Parse(Form), "Form1").Code;

        var body = written.Substring(written.IndexOf("Public Class", StringComparison.Ordinal));

        Assert.DoesNotContain("Option Explicit", body);
    }

    [Fact]
    public void TranslatesAModuleWithoutTouchingWhatItDeclares()
    {
        // A .bas is only a VB_Name attribute and code: Visual Basic 6 kept the
        // module's name in an attribute where Visual Basic .NET keeps it in
        // the Module statement, so the attribute has to be read and dropped
        // rather than passed through.
        const string module = """
            Attribute VB_Name = "Modulo1"
            Option Explicit

            Public Sub Avvia()
                Debug.Print "avviato"
            End Sub
            """;

        var body = string.Join("\n",
            module.Split('\n').Where(l => !l.TrimStart().StartsWith("Attribute VB_")));

        var translated = CodeTranslation.Translate(body).Code;

        Assert.Contains("Debug.Print(\"avviato\")", translated);
        Assert.DoesNotContain("Attribute VB_", translated);

        // And the Option statement, which the generated file declares in the
        // one place Visual Basic .NET accepts it.
        Assert.DoesNotContain("Option Explicit", translated);
    }

    [Fact]
    public void MovingAControlChangesOnlyTheLinesThatSayWhereItIs()
    {
        // The original is edited, not regenerated. A .frm holds more than this
        // converter understands — fonts, an OCX's property bag, the .frx
        // offsets pointing at binary beside it — and rewriting one from what
        // we read throws all of that away the first time somebody nudges a
        // button.
        var form = FormFile.Parse(Form);

        var moved = FormToAxaml.Convert(form)
            .Replace("x:Name=\"cmdOk\" Canvas.Left=\"88\" Canvas.Top=\"72\"",
                     "x:Name=\"cmdOk\" Canvas.Left=\"128\" Canvas.Top=\"92\"");

        var updated = AxamlToForm.Apply(Form, moved);

        var before = Form.Replace("\r\n", "\n").Split('\n');
        var after = updated.Replace("\r\n", "\n").Split('\n');

        var differences = before.Zip(after)
            .Where(pair => pair.First != pair.Second)
            .ToList();

        Assert.Equal(2, differences.Count);
        Assert.All(differences, d =>
            Assert.True(d.First.Contains("Left") || d.First.Contains("Top"),
                $"an unrelated line changed: {d.First.Trim()}"));

        // And the parts nobody understands are still there.
        Assert.Contains("BeginProperty ColumnHeader(1)", updated);
        Assert.Contains("MSComctlLib.ListView", updated);
    }

    [Fact]
    public void OpeningAndSavingWithoutTouchingAnythingChangesNothing()
    {
        // Twips do not survive the round trip through pixels — 4400 becomes
        // 293 becomes 4395 — so a form opened and saved without a single drag
        // came back with almost every size altered by a few twips, and the
        // diff was the whole file.
        var unchanged = AxamlToForm.Apply(Form, FormToAxaml.Convert(FormFile.Parse(Form)));

        Assert.Equal(Form, unchanged);
    }

    [Fact]
    public void KeepsTheLineEndingsTheFormWasWrittenWith()
    {
        // A .frm is CRLF. A file that comes back with different endings is a
        // whole-file change in every diff the author looks at afterwards.
        const string crlf = "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption = \"x\"\r\nEnd\r\n";

        var updated = AxamlToForm.Apply(
            crlf, FormToAxaml.Convert(FormFile.Parse(crlf)));

        Assert.Contains("\r\n", updated);
        Assert.DoesNotContain("\n\n", updated.Replace("\r\n", "\n").Replace("\n\n", "@"));
    }

    [Fact]
    public void SavingTheRealSampleLeavesItByteForByte()
    {
        // Against the file as Visual Basic 6 wrote it, CRLF and all: the
        // literal in this file has LF endings and no trailing newline, so it
        // exercises a different shape than the one anybody will actually open.
        var directory = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "samples", "Basalt.Sample.Vb6");

        var path = Path.Combine(directory, "Form1.frm");

        if (!File.Exists(path))
        {
            Assert.Skip("the sample form is not beside the tests");
            return;
        }

        var original = File.ReadAllText(path);

        var unchanged = AxamlToForm.Apply(
            original, FormToAxaml.Convert(FormFile.Parse(original)));

        Assert.Equal(original, unchanged);
    }

    [Fact]
    public void WritesAControlTheDesignerAdded()
    {
        // Without this a control drawn in the designer is lost the moment the
        // file is saved: drawn, visible, and gone, with nothing saying why.
        var markup = FormToAxaml.Convert(FormFile.Parse(Form))
            .Replace("</Canvas>",
                "<TextBox x:Name=\"txtNuovo\" Canvas.Left=\"20\" Canvas.Top=\"160\" " +
                "Width=\"100\" Height=\"20\" /></Canvas>");

        var updated = AxamlToForm.Apply(Form, markup);

        Assert.Contains("Begin VB.TextBox txtNuovo", updated);

        // Read back, not merely present: a control written in a shape the
        // parser cannot read is the same as one that was never written.
        var again = FormFile.Parse(updated);
        var added = again.Root.Children.FirstOrDefault(c => c.Name == "txtNuovo");

        Assert.NotNull(added);
        Assert.Equal("VB.TextBox", added!.Type);
        Assert.Equal(300, added.Number("Left"));
        Assert.Equal(1500, added.Number("Width"));
    }

    [Fact]
    public void KeepsTheFormStructurallyValidWhenAddingAControl()
    {
        // Every Begin needs its End, and a control written after the form's
        // own End is outside the form — Visual Basic 6 refuses to open the
        // file at all rather than showing it wrong.
        var markup = FormToAxaml.Convert(FormFile.Parse(Form))
            .Replace("</Canvas>",
                "<Button x:Name=\"cmdNuovo\" Canvas.Left=\"10\" Canvas.Top=\"10\" /></Canvas>");

        var lines = AxamlToForm.Apply(Form, markup)
            .Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .ToList();

        var begins = lines.Count(l =>
            l.StartsWith("Begin ", StringComparison.Ordinal));

        Assert.Equal(begins, lines.Count(l => l == "End"));

        // Inside the form: the last End is the form's.
        Assert.True(
            lines.FindIndex(l => l.Contains("cmdNuovo")) < lines.FindLastIndex(l => l == "End"),
            "the control was written outside the form");
    }

    [Fact]
    public void SurvivesTwoControlsWithTheSameName()
    {
        // Visual Basic 6 allowed it through control arrays. A duplicate took
        // the whole save down with an exception naming a dictionary key rather
        // than a form, which tells the author nothing about their file.
        var markup = FormToAxaml.Convert(FormFile.Parse(Form))
            .Replace("</Canvas>",
                "<TextBox x:Name=\"txtNome\" Canvas.Left=\"10\" Canvas.Top=\"10\" /></Canvas>");

        var updated = AxamlToForm.Apply(Form, markup);

        Assert.Contains("txtNome", updated);
    }

    [Fact]
    public void ReadsWhatComesAfterAControlWithSubProperties()
    {
        // A BeginProperty block is skipped, and the skip used to advance past
        // its EndProperty as well — taking the control's own End with it. The
        // stack stayed one deep for the rest of the file, so every control
        // after an OCX was read as being inside it. Nothing noticed while
        // nothing came after one.
        const string withOcx = """
            Begin VB.Form Form1 
               Begin MSComctlLib.ListView lvwElenco 
                  BeginProperty ColumnHeader(1) {BDD1F052-858B-11D1-B16A-00C0F0283628} 
                     Text            =   "Nome"
                  EndProperty
               End
               Begin VB.CommandButton cmdDopo 
                  Left            =   120
               End
            End
            """;

        var form = FormFile.Parse(withOcx);

        // Both on the form, not one inside the other.
        Assert.Equal(2, form.Root.Children.Count);
        Assert.Contains(form.Root.Children, c => c.Name == "cmdDopo");
        Assert.Empty(form.Root.Children.First(c => c.Name == "lvwElenco").Children);
    }
}
