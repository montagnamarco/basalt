using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using Basalt.Core.Settings;

namespace Basalt.Shell.Syntax;

/// <summary>
/// Syntax colouring driven by the same grammars Visual Studio Code uses.
///
/// Only for the languages Roslyn does not cover: HTML, CSS, JavaScript and the
/// Razor formats. Visual Basic keeps its Roslyn-based colouring, which knows
/// what the code means rather than only how it is spelt.
/// </summary>
public sealed class TextMateHighlighting : IDisposable
{
    private readonly RegistryOptions _options;
    private readonly TextMate.Installation _installation;
    private bool _disposed;

    private readonly VbHtmlRegistryOptions _registry;

    private TextMateHighlighting(TextEditor editor, ThemeName theme)
    {
        _options = new RegistryOptions(theme);

        // Wrapped so the .vbhtml grammar this project ships is served
        // alongside the ones the package provides.
        _registry = new VbHtmlRegistryOptions(_options);

        _installation = editor.InstallTextMate(_registry);
    }

    /// <summary>
    /// Installs colouring on an editor, or returns null for a file whose
    /// language is not one of these.
    ///
    /// Returning null rather than installing an inert grammar keeps the
    /// decision in one place: the caller asks, and either gets colouring or
    /// falls back to whatever it was doing before.
    /// </summary>
    public static TextMateHighlighting? InstallFor(
        TextEditor editor, string filePath, bool darkTheme = false)
    {
        var scope = ScopeFor(filePath);
        if (scope is null) return null;

        var highlighting = new TextMateHighlighting(
            editor, darkTheme ? ThemeName.DarkPlus : ThemeName.LightPlus);

        highlighting._installation.SetGrammar(scope);

        return highlighting;
    }

    /// <summary>
    /// Colours a file with the grammar theme that goes with the interface one.
    ///
    /// The pairing matters: Solarized panels around Dark+ syntax colours look
    /// like two themes at once.
    /// </summary>
    public static TextMateHighlighting? InstallFor(
        TextEditor editor, string filePath, AppTheme theme)
    {
        var scope = ScopeFor(filePath);
        if (scope is null) return null;

        var highlighting = new TextMateHighlighting(editor, GrammarThemeFor(theme));

        highlighting._installation.SetGrammar(scope);

        return highlighting;
    }

    /// <summary>
    /// The grammar theme that goes with an interface theme.
    ///
    /// TextMateSharp ships a counterpart for every theme this IDE offers, so
    /// each maps to a real one rather than to a light-or-dark approximation.
    /// </summary>
    public static ThemeName GrammarThemeFor(AppTheme theme) => theme switch
    {
        AppTheme.Dark => ThemeName.DarkPlus,
        AppTheme.Light => ThemeName.LightPlus,
        AppTheme.VisualStudioBlue => ThemeName.VisualStudioLight,
        AppTheme.HighContrastLight => ThemeName.HighContrastLight,
        AppTheme.HighContrastDark => ThemeName.HighContrastDark,
        AppTheme.SolarizedLight => ThemeName.SolarizedLight,
        AppTheme.SolarizedDark => ThemeName.SolarizedDark,

        // Following the system: which one that is has been decided elsewhere,
        // and the caller passes the resolved theme rather than System.
        _ => ThemeName.LightPlus
    };

    /// <summary>Whether this file is one TextMate should colour.</summary>
    public static bool Handles(string filePath) => ScopeFor(filePath) is not null;

    /// <summary>The grammar scope for a file, for tests and diagnostics.</summary>
    internal static string? ScopeForFile(string filePath) => ScopeFor(filePath);

    /// <summary>
    /// The grammar scope for a file.
    ///
    /// ".vbhtml" uses the grammar written for the VS Code extension, which
    /// this project ships: no such grammar exists elsewhere, since ASP.NET
    /// Core never supported the format. One grammar for both means an editor
    /// and the extension cannot colour the same file differently.
    ///
    /// It used to fall back to the C# Razor grammar when the file was not
    /// found on disk — markup and "@" transitions right, and the Visual Basic
    /// inside every code block coloured as C#. The grammar is embedded in the
    /// assembly now, so there is nothing to fall back from.
    /// </summary>
    private static string? ScopeFor(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension == ".vbhtml") return VbHtmlGrammar.ScopeName;

        // Roslyn colours Visual Basic, and it does so semantically.
        if (extension == ".vb") return null;

        if (!Handled.Contains(extension)) return null;

        var options = SharedOptions.Value;
        var language = options.GetLanguageByExtension(extension);

        return language is null ? null : options.GetScopeByLanguageId(language.Id);
    }

    /// <summary>
    /// The extensions this takes responsibility for.
    ///
    /// Listed rather than accepting everything the registry knows, so that
    /// adding a grammar does not silently take a file type away from a
    /// provider that handles it better.
    /// </summary>
    private static readonly HashSet<string> Handled = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml",
        ".css", ".scss", ".less",
        ".js", ".mjs", ".cjs", ".ts",
        ".json", ".xml",
        ".cshtml", ".razor", ".vbhtml",

        // C# is not a language Basalt supports any more, but a .cs file still
        // turns up in a solution — a project reference, something copied in.
        // TextMate colours it so it reads as code rather than as a wall of
        // grey; nothing else is offered for it.
        ".cs",

        // The query editor of the database panel writes into a .sql buffer,
        // so it gets the same colouring as any other language rather than a
        // second highlighter written for one panel.
        ".sql"
    };

    /// <summary>
    /// A registry shared for the lookups above.
    ///
    /// Building one costs reading the grammar index, which is wasted work if
    /// every file merely asked about builds its own.
    /// </summary>
    private static readonly Lazy<RegistryOptions> SharedOptions =
        new(() => new RegistryOptions(ThemeName.LightPlus));

    /// <summary>Switches between the light and dark themes.</summary>
    public void SetTheme(bool darkTheme) =>
        _installation.SetTheme(_options.LoadTheme(
            darkTheme ? ThemeName.DarkPlus : ThemeName.LightPlus));

    /// <summary>Switches to the grammar theme that goes with an interface one.</summary>
    public void SetTheme(AppTheme theme) =>
        _installation.SetTheme(_options.LoadTheme(GrammarThemeFor(theme)));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _installation.Dispose();
    }
}
