namespace Basalt.Core.Model;

/// <summary>
/// The languages the IDE understands.
///
/// Basalt is for Visual Basic and its dialects. C# was here and has been
/// taken out: a language the IDE half-supports is worse than one it does not
/// claim to support at all, and every feature written for both was written
/// twice.
/// </summary>
public enum SourceLanguage
{
    Unknown,
    VisualBasic
}

public static class SourceLanguageExtensions
{
    /// <summary>
    /// Infers the language from the extension of a source or project file.
    ///
    /// A .cs file comes back Unknown, which is the honest answer: it opens as
    /// text and the IDE says so rather than offering an experience it no
    /// longer has.
    /// </summary>
    public static SourceLanguage FromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".vb" or ".vbproj" => SourceLanguage.VisualBasic,
            _ => SourceLanguage.Unknown
        };

    /// <summary>Language name following the Roslyn convention (LanguageNames).</summary>
    public static string ToRoslynName(this SourceLanguage language) => language switch
    {
        SourceLanguage.VisualBasic => "Visual Basic",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Linguaggio non gestito.")
    };
}
