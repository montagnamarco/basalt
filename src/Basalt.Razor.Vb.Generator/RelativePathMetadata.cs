namespace Basalt.Razor.Vb.Generator;

/// <summary>
/// Validates the BasaltRelativePath item metadata before a generator trusts it.
/// </summary>
/// <remarks>
/// Roslyn's analyzer-config file is a plain .editorconfig, and its parser cuts
/// a value at an unescaped '#' or ';' — the characters that start a comment in
/// that format. A template at a path like Views/C#/Index.vbhtml, whose
/// BasaltRelativePath the package's props set to that same string, comes back
/// from <c>AnalyzerConfigOptions.TryGetValue</c> as "Views/C" alone. Used as
/// given, every file below that truncated folder would share one wrong
/// namespace or route, which is worse than not having the metadata at all.
///
/// Checked here, once, by the one fact a truncated value cannot have: it is
/// not a suffix of the template's own path. A project without '#' or ';' in
/// any of its folder names never notices this class exists.
/// </remarks>
internal static class RelativePathMetadata
{
    /// <summary>
    /// The metadata, trimmed, or null when missing, blank, or truncated: a
    /// generator that receives null falls back to its own comparison against
    /// BasaltProjectDir, exactly as it would without the metadata at all.
    /// </summary>
    public static string? Validate(string templatePath, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var trimmed = relativePath!.Trim();
        if (trimmed.Length == 0) return null;

        var normalisedPath = templatePath.Replace('\\', '/');
        var normalisedRelative = trimmed.Replace('\\', '/').TrimStart('/');

        // The whole path, or a suffix starting right after a separator: a
        // truncated "Views/C" is not a suffix of ".../Views/C#/Index.vbhtml"
        // at all, but checking only EndsWith would also pass a wrong match
        // like "C/Index.vbhtml" against ".../XC/Index.vbhtml".
        return string.Equals(normalisedPath, normalisedRelative, StringComparison.OrdinalIgnoreCase) ||
               normalisedPath.EndsWith("/" + normalisedRelative, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : null;
    }
}
