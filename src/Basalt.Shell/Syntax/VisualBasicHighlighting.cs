using System.Xml;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace Basalt.Shell.Syntax;

/// <summary>
/// The Visual Basic colouring for each interface theme.
///
/// AvaloniaEdit ships a VB definition whose colours are fixed for a white
/// page: keywords Blue, types a dark purple, the preprocessor Maroon. On the
/// dark theme's near-black editor those came out almost invisible, and no
/// theme switch could reach them, because the definition is shared and not
/// something the theme knows about.
///
/// The light theme keeps the built-in definition untouched. The dark themes
/// get a private copy of the same definition, read from AvaloniaEdit's own
/// resources, with only its colours replaced. Keyword lists, spans and the XML
/// documentation rules stay exactly as shipped, so the two cannot disagree
/// about what is a keyword.
/// </summary>
public static class VisualBasicHighlighting
{
    /// <summary>The name AvaloniaEdit gives its VB definition.</summary>
    private const string BuiltInName = "VB";

    private const string VbResource = "AvaloniaEdit.Highlighting.Resources.VB-Mode.xshd";
    private const string XmlDocResource = "AvaloniaEdit.Highlighting.Resources.XmlDoc.xshd";

    /// <summary>
    /// The colours for the dark themes, by the named colours of the definition.
    ///
    /// Each was measured against the dark themes' editor backgrounds (#1F1F1F,
    /// #000000 and Solarized Dark's #002B36) and reaches at least 4.5:1 on all
    /// three; the test that reads these back is in CodeEditorDarkThemeTests.
    /// The hues follow the Dark+ palette most editors use, so a VB file reads
    /// like the TextMate-coloured files beside it. Strings and literals share
    /// one colour, and so do the keyword families, as the definition groups them.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, Color> DarkVbColours =
        new Dictionary<string, Color>
        {
            ["Keywords"] = Color.Parse("#569CD6"),
            ["ContextKeywords"] = Color.Parse("#569CD6"),
            ["Constants"] = Color.Parse("#569CD6"),
            ["DataTypes"] = Color.Parse("#4EC9B0"),
            ["FunctionKeywords"] = Color.Parse("#DCDCAA"),
            // The word operators: New, And, Not, Is, AddressOf and the rest. In the
            // definition they come before the keywords, so New takes this colour;
            // close to the default text it made them read as plain identifiers.
            ["Operators"] = Color.Parse("#D8A0DF"),
            ["String"] = Color.Parse("#CE9178"),
            ["DateLiteral"] = Color.Parse("#CE9178"),
            ["Preprocessor"] = Color.Parse("#C586C0"),
            ["Comment"] = Color.Parse("#7CA668"),
        };

    /// <summary>The colours of the XML documentation definition the VB one imports.</summary>
    internal static readonly IReadOnlyDictionary<string, Color> DarkXmlDocColours =
        new Dictionary<string, Color>
        {
            ["DocComment"] = Color.Parse("#7CA668"),
            ["XmlString"] = Color.Parse("#CE9178"),
            ["XmlPunctuation"] = Color.Parse("#A6A6A6"),
            ["KnownDocTags"] = Color.Parse("#569CD6"),
        };

    /// <summary>
    /// Built once and shared by every dark-theme VB editor: after this it is
    /// only read, so sharing it is safe, and building it per editor would
    /// re-parse the keyword lists for every file opened.
    /// </summary>
    private static readonly Lazy<IHighlightingDefinition?> DarkDefinition = new(TryBuildDark);

    /// <summary>The VB definition for a theme: the built-in one, or the dark copy.</summary>
    /// <remarks>
    /// Falls back to the built-in definition when the dark copy cannot be built,
    /// so an AvaloniaEdit that renamed a resource or a colour leaves the dark
    /// theme with the light colours rather than an editor that fails to open.
    /// </remarks>
    public static IHighlightingDefinition? For(bool darkTheme) =>
        (darkTheme ? DarkDefinition.Value : null)
        ?? HighlightingManager.Instance.GetDefinition(BuiltInName);

    /// <summary>Builds the dark copy once; a failure is reported once and remembered as none.</summary>
    private static IHighlightingDefinition? TryBuildDark()
    {
        try
        {
            return BuildDark();
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException or HighlightingDefinitionInvalidException)
        {
            System.Diagnostics.Trace.TraceWarning($"Dark Visual Basic colours unavailable: {ex.Message}");
            return null;
        }
    }

    private static IHighlightingDefinition BuildDark()
    {
        // The VB definition imports the XML documentation rules by name. They
        // have to come from the copy below, or its colours would be the
        // built-in light ones and the doc comments would stay invisible.
        var xmlDoc = Load(XmlDocResource, new DarkResolver(null));
        Recolour(xmlDoc, DarkXmlDocColours);

        var vb = Load(VbResource, new DarkResolver(xmlDoc));
        Recolour(vb, DarkVbColours);

        return vb;
    }

    private static IHighlightingDefinition Load(
        string resource, IHighlightingDefinitionReferenceResolver resolver)
    {
        using var stream = typeof(TextEditor).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"AvaloniaEdit no longer ships {resource}; the dark VB colours cannot be built.");

        using var reader = XmlReader.Create(stream);

        return HighlightingLoader.Load(HighlightingLoader.LoadXshd(reader), resolver);
    }

    private static void Recolour(IHighlightingDefinition definition, IReadOnlyDictionary<string, Color> colours)
    {
        foreach (var (name, colour) in colours)
        {
            // A colour that is missing means AvaloniaEdit renamed it, and the
            // dark theme would quietly fall back to the light one. Fail instead.
            var named = definition.GetNamedColor(name)
                ?? throw new InvalidOperationException(
                    $"The {definition.Name} definition has no colour named {name}.");

            named.Foreground = new SimpleHighlightingBrush(colour);
        }
    }

    /// <summary>
    /// Resolves the one reference the VB definition makes, to the dark copy of
    /// the XML documentation rules, and everything else to the built-in set.
    /// </summary>
    private sealed class DarkResolver(IHighlightingDefinition? xmlDoc) : IHighlightingDefinitionReferenceResolver
    {
        public IHighlightingDefinition? GetDefinition(string name) =>
            name == "XmlDoc" && xmlDoc is not null
                ? xmlDoc
                : HighlightingManager.Instance.GetDefinition(name);

        public IHighlightingDefinition? GetDefinitionByExtension(string extension) =>
            HighlightingManager.Instance.GetDefinitionByExtension(extension);
    }
}
