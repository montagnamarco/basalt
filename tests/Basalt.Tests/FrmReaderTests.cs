using Basalt.Designer.VisualBasic6;

namespace Basalt.Tests;

/// <summary>
/// Reading a VB6 .frm file.
///
/// Written before any designer work, because the plan asks to understand the
/// format before designing for it, and the way to understand a format is to
/// read one rather than to describe it from memory.
/// </summary>
public sealed class FrmReaderTests
{
    /// <summary>
    /// A form of the shape VB6 writes.
    ///
    /// Nested controls, a BeginProperty block for the font, the Attribute
    /// lines VB6 puts after the form, and the code behind: all the parts that
    /// a reader has to get past.
    /// </summary>
    private const string Realistic = """
        VERSION 5.00
        Begin VB.Form frmMain
           Caption         =   "My Program"
           ClientHeight    =   3600
           ClientLeft      =   120
           ClientTop       =   450
           ClientWidth     =   4800
           LinkTopic       =   "Form1"
           ScaleHeight     =   3600
           ScaleWidth      =   4800
           StartUpPosition =   3  'Windows Default
           Begin VB.Frame fraOptions
              Caption         =   "Options"
              Height          =   1215
              Left            =   240
              TabIndex        =   3
              Top             =   1680
              Width           =   4335
              Begin VB.CheckBox chkLoud
                 Caption         =   "Loud"
                 Height          =   255
                 Left            =   120
                 TabIndex        =   4
                 Top             =   360
                 Width           =   1215
              End
           End
           Begin VB.CommandButton cmdGo
              Caption         =   "Go"
              BeginProperty Font
                 Name            =   "MS Sans Serif"
                 Size            =   8.25
                 Charset         =   0
                 Weight          =   700
              EndProperty
              Height          =   375
              Left            =   3480
              TabIndex        =   2
              Top             =   960
              Width           =   1095
           End
           Begin VB.TextBox txtName
              Height          =   285
              Left            =   1200
              TabIndex        =   0
              Top             =   360
              Width           =   3375
           End
           Begin VB.Label lblName
              Caption         =   "Name:"
              Height          =   255
              Left            =   240
              TabIndex        =   1
              Top             =   400
              Width           =   855
           End
        End
        Attribute VB_Name = "frmMain"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = False
        Option Explicit

        Private Sub cmdGo_Click()
            MsgBox "Hello, " & txtName.Text
        End Sub

        Private Sub Form_Load()
            txtName.Text = ""
        End Sub
        """;

    private static FrmForm Read(string text = Realistic)
    {
        var (form, problem) = FrmReader.Read(text);

        Assert.True(form is not null, problem);

        return form;
    }

    [Fact]
    public void ReadsTheFormItself()
    {
        var form = Read();

        Assert.Equal("frmMain", form.Name);
        Assert.Equal("VB.Form", form.TypeName);
        Assert.Equal("My Program", form.Root.Text("Caption"));
    }

    [Fact]
    public void ReadsEveryControlIncludingNestedOnes()
    {
        var names = Read().Controls.Select(c => c.Name).ToList();

        Assert.Contains("cmdGo", names);
        Assert.Contains("txtName", names);
        Assert.Contains("lblName", names);
        Assert.Contains("fraOptions", names);

        // The check box is inside the frame, and still has to be found.
        Assert.Contains("chkLoud", names);
    }

    [Fact]
    public void KeepsAControlInsideTheOneItBelongsTo()
    {
        // Nesting is what decides where a control is drawn, so losing it
        // would put the check box on the form rather than in the frame.
        var frame = Read().Controls.Single(c => c.Name == "fraOptions");

        Assert.Equal("chkLoud", Assert.Single(frame.Children).Name);
    }

    [Fact]
    public void ReadsThePositionsAsNumbers()
    {
        var button = Read().Controls.Single(c => c.Name == "cmdGo");

        Assert.Equal(3480, button.Number("Left"));
        Assert.Equal(960, button.Number("Top"));
        Assert.Equal(1095, button.Number("Width"));
        Assert.Equal(375, button.Number("Height"));
    }

    [Fact]
    public void GetsPastABeginPropertyBlockWithoutLosingWhatFollows()
    {
        // The font block sits between Caption and Height; a reader that does
        // not step over it reads the font's Name as the button's.
        var button = Read().Controls.Single(c => c.Name == "cmdGo");

        Assert.Equal("Go", button.Text("Caption"));
        Assert.Equal(375, button.Number("Height"));
        Assert.False(button.Properties.ContainsKey("Charset"));
    }

    [Fact]
    public void KeepsTheCodeThatFollowsTheForm()
    {
        var form = Read();

        Assert.Contains("MsgBox", form.Code, StringComparison.Ordinal);
        Assert.Contains("Option Explicit", form.Code, StringComparison.Ordinal);

        // The Attribute lines are VB6 bookkeeping, not code.
        Assert.DoesNotContain("VB_GlobalNameSpace", form.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void FindsWhichControlEachHandlerBelongsTo()
    {
        // VB6 ties a handler to a control by its name, which is what makes a
        // form and its code one thing rather than two files.
        var handlers = Read().Handlers;

        Assert.Contains(("cmdGo", "Click"), handlers);
        Assert.Contains(("Form", "Load"), handlers);
    }

    [Fact]
    public void SaysWhatIsWrongWithAFileItCannotRead()
    {
        var (form, problem) = FrmReader.Read("""
            VERSION 5.00
            Begin VB.Form frmMain
               Caption = "Never closed"
            """);

        Assert.Null(form);
        Assert.Contains("never closed", problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaysSoWhenThereIsNoFormAtAll()
    {
        var (form, problem) = FrmReader.Read("Option Explicit\nPrivate Sub Thing()\nEnd Sub");

        Assert.Null(form);
        Assert.Contains("no form", problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertsTwipsTheWayVb6MeantThem()
    {
        // VB6 measures in twips: 1440 to the inch, 15 to a pixel at 96 dpi.
        // Avalonia's unit is 1/96 inch, so the two agree at that scale.
        Assert.Equal(96, FrmReader.TwipsToPixels(1440));
        Assert.Equal(1440, FrmReader.PixelsToTwips(96));

        // The button above is 1095 twips wide, which is 73 pixels.
        Assert.Equal(73, FrmReader.TwipsToPixels(1095));
    }

    [Fact]
    public void ReadsAFormWithNothingOnIt()
    {
        var (form, problem) = FrmReader.Read("""
            VERSION 5.00
            Begin VB.Form frmEmpty
               Caption = "Empty"
            End
            """);

        Assert.True(form is not null, problem);
        Assert.Empty(form.Controls);
        Assert.Equal("frmEmpty", form.Name);
    }
}
