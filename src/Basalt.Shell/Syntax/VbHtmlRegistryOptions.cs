using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace Basalt.Shell.Syntax;

/// <summary>
/// The grammars from the package, plus the one this project writes.
///
/// TextMateSharp ships grammars for the languages Visual Studio Code knows,
/// which does not include Razor for Visual Basic — nobody wrote one, because
/// ASP.NET Core cannot compile the format. This serves ours for that scope and
/// hands everything else to the package unchanged.
/// </summary>
public sealed class VbHtmlRegistryOptions : IRegistryOptions
{
    private readonly RegistryOptions _inner;
    private readonly Lazy<IRawGrammar?> _vbHtml;

    public VbHtmlRegistryOptions(RegistryOptions inner)
    {
        _inner = inner;
        _vbHtml = new Lazy<IRawGrammar?>(LoadVbHtml);
    }

    public IRawGrammar? GetGrammar(string scopeName) =>
        scopeName == VbHtmlGrammar.ScopeName
            ? _vbHtml.Value
            : _inner.GetGrammar(scopeName);

    public ICollection<string> GetInjections(string scopeName) =>
        _inner.GetInjections(scopeName);

    public IRawTheme GetTheme(string scopeName) => _inner.GetTheme(scopeName);

    public IRawTheme GetDefaultTheme() => _inner.GetDefaultTheme();

    /// <summary>
    /// Reads the grammar file.
    ///
    /// A grammar that will not parse is treated as absent rather than thrown:
    /// the caller then falls back to the C# Razor grammar, which colours a
    /// view imperfectly but colours it.
    /// </summary>
    private IRawGrammar? LoadVbHtml()
    {
        if (VbHtmlGrammar.Path is not { } path) return null;

        try
        {
            // The reader wants a StreamReader, so the file is opened rather
            // than read into a string first.
            using var reader = new StreamReader(path);

            return GrammarReader.ReadGrammarSync(reader);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or InvalidOperationException
                                      or FormatException or ArgumentException)
        {
            return null;
        }
    }
}
