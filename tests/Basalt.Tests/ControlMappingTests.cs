using Basalt.Designer.VisualBasic6;

namespace Basalt.Tests;

/// <summary>
/// What a VB6 control becomes, and what it cannot.
///
/// The refusals matter as much as the mappings: a designer that quietly drops
/// an OLE control produces a form that looks converted and is not.
/// </summary>
public sealed class ControlMappingTests
{
    [Fact]
    public void KnowsTheCommonControls()
    {
        Assert.Equal("Button", ControlMappings.For("VB.CommandButton")?.Avalonia);
        Assert.Equal("TextBox", ControlMappings.For("VB.TextBox")?.Avalonia);
        Assert.Equal("CheckBox", ControlMappings.For("VB.CheckBox")?.Avalonia);
        Assert.Equal("ListBox", ControlMappings.For("VB.ListBox")?.Avalonia);
    }

    [Fact]
    public void MapsTheFormItself()
    {
        var form = ControlMappings.For("VB.Form");

        Assert.Equal("Window", form?.Avalonia);
        Assert.Equal(MappingFidelity.Direct, form?.Fidelity);
    }

    [Fact]
    public void SaysPlainlyWhatCannotBeReproduced()
    {
        // Each of these would need a Windows facility that does not exist on
        // the platforms this IDE runs on.
        foreach (var name in new[] { "VB.OLE", "VB.Data", "VB.DriveListBox" })
        {
            var mapping = ControlMappings.For(name);

            Assert.Equal(MappingFidelity.Unsupported, mapping?.Fidelity);
            Assert.Null(mapping?.Avalonia);
            Assert.False(string.IsNullOrWhiteSpace(mapping?.Note));
        }
    }

    [Fact]
    public void GivesAReasonForEveryThingItCannotDo()
    {
        // An unsupported control without a reason is a shrug.
        Assert.All(ControlMappings.Unsupported,
            mapping => Assert.False(string.IsNullOrWhiteSpace(mapping.Note)));
    }

    [Fact]
    public void TreatsTheTimerAsSomethingOtherThanAControl()
    {
        // VB6 places it on the form; Avalonia has no such thing on a window.
        var timer = ControlMappings.For("VB.Timer");

        Assert.Equal(MappingFidelity.Composed, timer?.Fidelity);
        Assert.Contains("field", timer?.Note ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaysNothingAboutAControlItHasNeverSeen()
    {
        Assert.Null(ControlMappings.For("MSComctlLib.TreeView"));
    }

    [Fact]
    public void WarnsAboutWhatAFormWouldLose()
    {
        var (form, problem) = FrmReader.Read("""
            VERSION 5.00
            Begin VB.Form frmMain
               Caption = "Test"
               Begin VB.CommandButton cmdGo
                  Caption = "Go"
               End
               Begin VB.OLE oleThing
                  Height = 500
               End
               Begin MSComctlLib.TreeView treeThing
                  Height = 500
               End
            End
            """);

        Assert.True(form is not null, problem);

        var problems = ControlMappings.ProblemsWith(form);

        // The button is fine; the other two are not, and each is named.
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("oleThing", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("treeThing", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("cmdGo", StringComparison.Ordinal));
    }

    [Fact]
    public void SaysAnUnknownControlIsProbablyFromAnOcx()
    {
        var (form, _) = FrmReader.Read("""
            VERSION 5.00
            Begin VB.Form f
               Begin MSComctlLib.ListView lv
                  Height = 100
               End
            End
            """);

        Assert.Contains("ocx", ControlMappings.ProblemsWith(form!).Single(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindsNothingWrongWithAFormOfOrdinaryControls()
    {
        var (form, _) = FrmReader.Read("""
            VERSION 5.00
            Begin VB.Form f
               Begin VB.CommandButton b
                  Caption = "Go"
               End
               Begin VB.Label l
                  Caption = "Name"
               End
            End
            """);

        Assert.Empty(ControlMappings.ProblemsWith(form!));
    }
}
