using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Syntax;
using Basalt.Shell.ViewModels;
using Dock.Avalonia.Controls;

namespace Basalt.Tests;

/// <summary>
/// Visual Basic colours in the dark themes, read back from rendered pixels.
///
/// The defect: AvaloniaEdit's VB definition fixes its colours for a white
/// page, so keywords, types and the preprocessor were near-invisible on the
/// dark editor, and switching theme changed nothing for an open .vb file.
/// A brush resolving to the right colour says nothing about what reaches the
/// screen, so each check rasterises a real editor and measures the glyph
/// pixels against the background they were drawn on.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class CodeEditorDarkThemeTests : IDisposable
{
    /// <summary>WCAG AA for body text.</summary>
    private const double TextContrast = 4.5;

    /// <summary>WCAG 1.4.11: a control's glyph against its surroundings.</summary>
    private const double GlyphContrast = 3.0;

    private const string Source = "Module Program\n"
        + "    Dim total As Integer = 42 ' sum\n"
        + "    Dim text = \"hello\"\n"
        + "    Dim made As New Object\n"
        + "End Module\n";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-editor-dark-" + Guid.NewGuid().ToString("N"));

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-editor-dark-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    // Restored on Dispose: a test that leaves Dark behind makes the next one
    // that assumes Light fail, depending on run order.
    private readonly ThemeVariant? _previousVariant =
        Application.Current?.RequestedThemeVariant;

    public CodeEditorDarkThemeTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);
        if (Application.Current is { } app) app.RequestedThemeVariant = _previousVariant;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    private void SaveTheme(AppTheme theme)
    {
        var store = new SettingsStore(_settings);
        var settings = store.Load();
        settings.Appearance.Theme = theme;
        store.Save(settings);
    }

    private string WriteProgram()
    {
        var file = Path.Combine(_root, "Program.vb");
        File.WriteAllText(file, Source);
        return file;
    }

    private static Color Token(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), key);
        return (Color)value!;
    }

    /// <summary>
    /// The rectangle a span of text covers, in the coordinates of <paramref name="root"/>,
    /// so it can be read from a raster of that visual.
    /// </summary>
    private static PixelRect TokenArea(Visual root, TextEditor text, int offset, int length)
    {
        var view = text.TextArea.TextView;
        var start = text.Document.GetLocation(offset);
        var end = text.Document.GetLocation(offset + length);

        var from = view.GetVisualPosition(
            new TextViewPosition(start.Line, start.Column), VisualYPosition.LineTop);
        var to = view.GetVisualPosition(
            new TextViewPosition(end.Line, end.Column), VisualYPosition.LineBottom);

        var topLeft = view.TranslatePoint(from, root) ?? from;
        var bottomRight = view.TranslatePoint(to, root) ?? to;

        return new PixelRect(
            (int)Math.Floor(topLeft.X), (int)Math.Floor(topLeft.Y),
            (int)Math.Ceiling(bottomRight.X - topLeft.X),
            (int)Math.Ceiling(bottomRight.Y - topLeft.Y));
    }

    private static TextEditor TextOf(CodeEditor editor) =>
        editor.GetVisualDescendants().OfType<TextEditor>().Single();

    /// <summary>
    /// Rasterises the window and measures one token in the editor: its area,
    /// the background it sits on and the best contrast any of its pixels
    /// reaches against that background.
    /// </summary>
    /// <remarks>
    /// The window, not the editor, is rasterised: the editor leaves its own
    /// unpainted pixels transparent, and the window is what paints the
    /// background the user sees behind the text.
    /// </remarks>
    private static (PixelRect Area, Color Background, double Contrast, Raster Raster) Measure(
        Window window, CodeEditor editor, string token)
    {
        var text = TextOf(editor);
        var index = text.Text.IndexOf(token, StringComparison.Ordinal);

        Assert.True(index >= 0, $"'{token}' is not in the editor");

        var raster = Raster.Of(window);
        var area = TokenArea(window, text, index, token.Length);

        // A blank stretch of the same text row, well right of the text.
        var rowRight = text.TranslatePoint(new Point(text.Bounds.Width - 40, 0), window) ?? default;
        var background = raster.At((int)Math.Round(rowRight.X), area.Y + area.Height / 2);

        // The glyph's own colour: the one most pixels of the token share. The
        // best pixel would be the caret or a stray highlight, not the text.
        var ink = InkOf(raster, area, background);
        var contrast = Raster.Contrast(ink, background);

        return (area, background, contrast, raster);
    }

    /// <summary>
    /// The colour most of an area's pixels share, among those that differ
    /// visibly from the background: a glyph's stems are fully covered, so
    /// they carry its true colour while the anti-aliased edges vary.
    /// </summary>
    private static Color InkOf(Raster raster, PixelRect area, Color background)
    {
        var counts = new Dictionary<Color, int>();

        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
            {
                var pixel = raster.At(x, y);
                if (Raster.Contrast(pixel, background) < 1.5) continue;

                counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
            }

        Assert.NotEmpty(counts);

        return counts.MaxBy(pair => pair.Value).Key;
    }

    private async Task<(TestWindow Host, CodeEditor Editor, string File)> OpenProgramAsync()
    {
        var file = WriteProgram();
        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(file);
        await host.SettleAsync(10);

        var editor = host.Window.CodeEditorForTests(file);
        Assert.NotNull(editor);

        return (host, editor!, file);
    }

    // ------------------------------------------------------------------
    // The palette itself, against every dark background the IDE has.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public void EveryDarkColourReachesTheContrastOnEveryDarkBackground()
    {
        var failures = new List<string>();

        foreach (var (theme, variant) in new[]
        {
            ("Dark", ThemeVariant.Dark),
            ("HighContrastDark", IdeThemes.HighContrastDark),
            ("SolarizedDark", IdeThemes.SolarizedDark),
        })
        {
            var background = Token("EditorBackground", variant);

            foreach (var (name, colour) in VisualBasicHighlighting.DarkVbColours
                         .Concat(VisualBasicHighlighting.DarkXmlDocColours))
            {
                var ratio = Raster.Contrast(colour, background);
                if (ratio < TextContrast)
                    failures.Add($"{theme} {name} {colour}: {ratio:0.00}:1");
            }
        }

        Assert.True(failures.Count == 0,
            "These dark colours are below 4.5:1:\n" + string.Join("\n", failures));
    }

    // ------------------------------------------------------------------
    // Rendered editors.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ADarkEditorDrawsEveryTokenKindWithEnoughContrast()
    {
        SaveTheme(AppTheme.Dark);

        var (host, editor, _) = await OpenProgramAsync();
        using var dispose = host;

        var checks = new (string Token, string Kind)[]
        {
            ("Module", "keyword"),
            ("Integer", "type"),
            ("\"hello\"", "string"),
            ("' sum", "comment"),
            ("42", "number"),
            ("total", "identifier"),
            ("=", "operator"),
        };

        var failures = new List<string>();

        foreach (var (token, kind) in checks)
        {
            var (_, _, contrast, _) = Measure(host.Window, editor, token);
            if (contrast < TextContrast) failures.Add($"{kind} '{token}': {contrast:0.00}:1");
        }

        Assert.True(failures.Count == 0,
            "Dark theme tokens below 4.5:1:\n" + string.Join("\n", failures));
    }

    [AvaloniaFact]
    public async Task ADarkKeywordIsPaintedInItsOwnColour()
    {
        SaveTheme(AppTheme.Dark);

        var (host, editor, _) = await OpenProgramAsync();
        using var dispose = host;

        var (area, background, contrast, raster) = Measure(host.Window, editor, "Module");

        Assert.True(contrast >= TextContrast,
            $"'Module' reaches {contrast:0.00}:1 against {background}, below 4.5:1");

        // The contrast alone could be met by any light colour. Reading the
        // keyword's own colour proves the palette reached the glyphs.
        var keyword = VisualBasicHighlighting.DarkVbColours["Keywords"];
        Assert.True(raster.Contains(area, keyword, tolerance: 24),
            $"'Module' has none of the keyword colour {keyword}");
    }

    /// <remarks>
    /// The word operators (New, And, Not, Is…) are a colour of their own in the
    /// definition, declared before the keywords, so New takes it. Mapped close
    /// to the default text, "As New Object" read as plain identifiers.
    /// Reintroduced once (#D4D4D4): this test failed.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADarkWordOperatorIsPaintedInItsOwnColour()
    {
        SaveTheme(AppTheme.Dark);

        var (host, editor, _) = await OpenProgramAsync();
        using var dispose = host;

        var (area, background, contrast, raster) = Measure(host.Window, editor, "New");

        Assert.True(contrast >= TextContrast, $"'New' reaches {contrast:0.00}:1 against {background}, below 4.5:1");
        var operators = VisualBasicHighlighting.DarkVbColours["Operators"];
        Assert.True(raster.Contains(area, operators, tolerance: 24), $"'New' has none of the operator colour {operators}");
        // Plain text in the dark theme is a grey; a grey operator reads as plain
        // text however far its shade is from the default. The glyph's own colour
        // must carry a hue: its channels apart by a clear margin.
        var ink = InkOf(raster, area, background);
        var spread = Math.Max(ink.R, Math.Max(ink.G, ink.B)) - Math.Min(ink.R, Math.Min(ink.G, ink.B));
        Assert.True(spread >= 40, $"'New' is drawn in a grey ({ink}), like plain text");
    }

    [AvaloniaFact]
    public async Task LightThemeKeepsTheBuiltInColours()
    {
        SaveTheme(AppTheme.Light);

        var (host, editor, _) = await OpenProgramAsync();
        using var dispose = host;

        var (area, _, _, raster) = Measure(host.Window, editor, "Module");

        // AvaloniaEdit's Blue, which the light theme has always used.
        Assert.True(raster.Contains(area, Colors.Blue, tolerance: 24),
            "the light keyword is no longer AvaloniaEdit's Blue");
    }

    [AvaloniaFact]
    public async Task SwitchingToDarkRecoloursAnOpenFile()
    {
        SaveTheme(AppTheme.Light);

        var (host, editor, _) = await OpenProgramAsync();
        using var dispose = host;

        // Open in the light theme, as the file was.
        var (_, lightBg, lightContrast, _) = Measure(host.Window, editor, "Module");
        Assert.True(lightContrast >= TextContrast, $"the light theme was already unreadable: {lightContrast:0.00} against {lightBg}");

        // The Settings window's path: the same call the window makes on Apply.
        var settings = new SettingsStore(_settings).Load();
        settings.Appearance.Theme = AppTheme.Dark;
        host.Window.ApplySettingsForTests(settings);
        await host.SettleAsync(5);

        var (area, background, darkContrast, raster) = Measure(host.Window, editor, "Module");

        Assert.True(darkContrast >= TextContrast,
            $"after switching to dark, 'Module' reaches {darkContrast:0.00}:1 against {background}");
        Assert.True(raster.Contains(area, VisualBasicHighlighting.DarkVbColours["Keywords"], tolerance: 24),
            "after switching to dark, 'Module' is still in the light keyword colour");
    }

    [AvaloniaFact]
    public async Task SwitchingBackToLightRecoloursAnOpenFile()
    {
        SaveTheme(AppTheme.Dark);

        var (host, editor, _) = await OpenProgramAsync();
        using var dispose = host;

        var settings = new SettingsStore(_settings).Load();
        settings.Appearance.Theme = AppTheme.Light;
        host.Window.ApplySettingsForTests(settings);
        await host.SettleAsync(5);

        var (area, _, _, raster) = Measure(host.Window, editor, "Module");

        Assert.True(raster.Contains(area, Colors.Blue, tolerance: 24),
            "after switching back to light, 'Module' is not the light keyword colour");
    }

    // ------------------------------------------------------------------
    // The close button on a document tab.
    // ------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public async Task EachDocumentTabHasADrawnCloseButton(AppTheme theme)
    {
        SaveTheme(theme);

        var (host, _, file) = await OpenProgramAsync();
        using var dispose = host;

        var tab = host.Window.GetVisualDescendants()
            .OfType<DocumentTabStripItem>()
            .Single(t => (t.DataContext as Basalt.Shell.Docking.IdeDocument)?.Document.FilePath == file);

        var glyph = tab.GetVisualDescendants()
            .OfType<Avalonia.Controls.Shapes.Path>()
            .Single(p => p.Name == "PART_ClosePath");

        var button = glyph.FindAncestorOfType<Button>()!;

        // The defect was a grey square with a glyph laid out 0 pixels wide.
        Assert.True(glyph.Bounds.Width >= 8 && glyph.Bounds.Height >= 8,
            $"the close glyph is {glyph.Bounds.Width:0}x{glyph.Bounds.Height:0}, not drawn");

        // Transparent, so the button shows the tab rather than a grey box.
        Assert.True(button.Background is null
            || (button.Background is ISolidColorBrush { Color.A: 0 }),
            $"the close button has a fill of {button.Background}");

        var raster = Raster.Of(host.Window);
        var tabArea = Raster.BoundsIn(tab, host.Window);
        var glyphArea = Raster.BoundsIn(glyph, host.Window);
        var background = raster.At(tabArea.Right - 4, tabArea.Bottom - 4);

        var contrast = raster.BestContrastAgainst(glyphArea, background);
        Assert.True(contrast >= GlyphContrast,
            $"the close glyph reaches {contrast:0.00}:1 against the tab, below 3:1");

        // A glyph has strokes. A stray pixel or a filled block is not an X.
        var strokes = 0;
        for (var y = glyphArea.Y; y < glyphArea.Bottom; y++)
            for (var x = glyphArea.X; x < glyphArea.Right; x++)
                if (Raster.Contrast(raster.At(x, y), background) >= GlyphContrast) strokes++;

        Assert.True(strokes >= 8, $"only {strokes} pixels of the close glyph are drawn");
    }
}
