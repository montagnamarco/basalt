using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>The icon set.</summary>
public class IdeIconsTests
{
    [Theory]
    [InlineData("Program.vb", IconKind.VisualBasicFile)]
    [InlineData("Program.cs", IconKind.CSharpFile)]
    [InlineData("Index.vbhtml", IconKind.RazorFile)]
    [InlineData("page.html", IconKind.WebFile)]
    [InlineData("site.css", IconKind.StyleFile)]
    [InlineData("app.js", IconKind.ScriptFile)]
    [InlineData("appsettings.json", IconKind.JsonFile)]
    [InlineData("Project.vbproj", IconKind.XmlFile)]
    [InlineData("Window.axaml", IconKind.DesignerFile)]
    [InlineData("logo.png", IconKind.ImageFile)]
    public void PicksTheIconAFileCallsFor(string fileName, IconKind expected)
    {
        Assert.Equal(expected, IdeIcons.ForFile("/a/" + fileName));
    }

    [Fact]
    public void FallsBackToAPlainDocument()
    {
        Assert.Equal(IconKind.TextFile, IdeIcons.ForFile("/a/notes.unknown"));
    }

    [Fact]
    public void MatchesExtensionsWhateverTheirCase()
    {
        Assert.Equal(IconKind.VisualBasicFile, IdeIcons.ForFile("/a/PROGRAM.VB"));
    }

    [Theory]
    [InlineData(IconKind.Save)]
    [InlineData(IconKind.Run)]
    [InlineData(IconKind.Debug)]
    [InlineData(IconKind.Branch)]
    [InlineData(IconKind.Settings)]
    [InlineData(IconKind.Folder)]
    public void DrawsEveryIconItNames(IconKind kind)
    {
        // The path data rather than the geometry: turning one into the other
        // needs a render backend, which a plain unit test has no reason to
        // start. That the data really parses is checked separately.
        Assert.NotNull(IdeIcons.PathDataFor(kind));
    }

    [AvaloniaFact]
    public void TurnsEveryPathIntoGeometryThatParses()
    {
        // Here with a backend running, so a path that is not valid geometry
        // fails rather than passing as a non-null string.
        foreach (var kind in Enum.GetValues<IconKind>().Where(k => k != IconKind.None))
            Assert.NotNull(IdeIcons.PathFor(kind));
    }

    [Fact]
    public void EveryKindExceptNoneHasAShape()
    {
        // A kind with no shape would render as an empty gap, which reads as a
        // bug rather than as a deliberate blank.
        var missing = Enum.GetValues<IconKind>()
            .Where(k => k != IconKind.None)
            .Where(k => IdeIcons.PathDataFor(k) is null)
            .ToList();

        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public void HasAnApplicationIconThatIsAVolcano()
    {
        // Basalt is volcanic rock, and the icon says so. Checked as real
        // geometry rather than as a non-empty string: a malformed path is a
        // blank square at run time.
        var geometry = IdeIcons.PathFor(IconKind.Application);

        Assert.NotNull(geometry);

        // Drawn in the same 16 by 16 box as every other icon, so it scales
        // with them.
        Assert.True(geometry.Bounds.Width <= 16, $"Too wide: {geometry.Bounds}");
        Assert.True(geometry.Bounds.Height <= 16, $"Too tall: {geometry.Bounds}");
        Assert.True(geometry.Bounds.Width > 8, $"Too small to read: {geometry.Bounds}");
    }

    [Fact]
    public void TheApplicationIconCarriesTheColourOfLava()
    {
        Assert.NotNull(IdeIcons.AccentFor(IconKind.Application));
    }

    [Fact]
    public void DrawsNothingForNone()
    {
        Assert.Null(IdeIcons.PathDataFor(IconKind.None));
    }

    [Fact]
    public void ColoursTheIconsThatCarryMeaning()
    {
        // Red for a breakpoint, green for run: the colour is the fastest part
        // of the icon to read.
        Assert.NotNull(IdeIcons.AccentFor(IconKind.Breakpoint));
        Assert.NotNull(IdeIcons.AccentFor(IconKind.Run));
        Assert.NotNull(IdeIcons.AccentFor(IconKind.VisualBasicFile));
    }

    [Fact]
    public void LeavesNeutralIconsToTheTheme()
    {
        // These must follow the foreground colour, or a dark theme shows black
        // glyphs on a dark panel.
        Assert.Null(IdeIcons.AccentFor(IconKind.Copy));
        Assert.Null(IdeIcons.AccentFor(IconKind.Undo));
    }
}

/// <summary>The toolbar.</summary>
public class IdeToolbarTests
{
    [AvaloniaFact]
    public void StartsEmpty()
    {
        Assert.Equal(0, new IdeToolbar().ButtonCount);
    }

    [AvaloniaFact]
    public void ShowsAButtonForEachAction()
    {
        var toolbar = new IdeToolbar();

        toolbar.Show([
            new ToolbarAction(IconKind.Save, "Save", () => { }),
            new ToolbarAction(IconKind.Build, "Build", () => { })
        ]);

        Assert.Equal(2, toolbar.ButtonCount);
    }

    [AvaloniaFact]
    public void ReplacesWhatItShowsRatherThanAddingToIt()
    {
        var toolbar = new IdeToolbar();

        toolbar.Show([new ToolbarAction(IconKind.Save, "Save", () => { })]);
        toolbar.Show([new ToolbarAction(IconKind.Run, "Run", () => { })]);

        Assert.Equal(1, toolbar.ButtonCount);
    }

    [AvaloniaFact]
    public void SeparatorsDoNotCountAsButtons()
    {
        var toolbar = new IdeToolbar();

        toolbar.Show([
            new ToolbarAction(IconKind.Save, "Save", () => { }),
            new ToolbarAction(IconKind.Run, "Run", () => { }) { StartsGroup = true }
        ]);

        Assert.Equal(2, toolbar.ButtonCount);
    }
}

/// <summary>The status bar.</summary>
public class IdeStatusBarTests
{
    [AvaloniaFact]
    public void ShowsWhereTheCaretIs()
    {
        var bar = new IdeStatusBar();

        bar.ShowPosition(12, 5);

        Assert.Equal("Ln 12, Col 5", bar.Position);
    }

    [AvaloniaFact]
    public void SaysHowMuchIsSelected()
    {
        var bar = new IdeStatusBar();

        bar.ShowPosition(12, 5, selectionLength: 40);

        Assert.Contains("40 selected", bar.Position);
    }

    [AvaloniaFact]
    public void LeavesOutTheSelectionWhenThereIsNone()
    {
        var bar = new IdeStatusBar();

        bar.ShowPosition(1, 1, selectionLength: 0);

        Assert.DoesNotContain("selected", bar.Position);
    }

    [AvaloniaFact]
    public void ShowsHowTheFileIsStored()
    {
        var bar = new IdeStatusBar();

        bar.ShowFile("UTF-8", "LF", "Visual Basic");

        Assert.Equal("UTF-8", bar.Encoding);
        Assert.Equal("LF", bar.LineEnding);
        Assert.Equal("Visual Basic", bar.Language);
    }

    [AvaloniaFact]
    public void EmptiesTheFileDetailsWhenNothingIsOpen()
    {
        var bar = new IdeStatusBar();
        bar.ShowFile("UTF-8", "LF", "Visual Basic");

        bar.Clear();

        Assert.Equal("", bar.Encoding);
    }

    [Theory]
    [InlineData("a\nb", "LF")]
    [InlineData("a\r\nb", "CRLF")]
    [InlineData("a\r\nb\nc", "Mixed")]
    [InlineData("no line endings", "LF")]
    public void NamesTheLineEndingADocumentUses(string text, string expected)
    {
        // A file with both is worth reporting as mixed before saving over it.
        Assert.Equal(expected, IdeStatusBar.DescribeLineEnding(text));
    }
}
