namespace Basalt.Extensibility;

/// <summary>
/// A language the IDE can work with.
///
/// An identity rather than an enumeration value: a language shipped separately
/// has to be able to describe itself, which a closed enum cannot allow.
/// </summary>
public sealed record LanguageIdentity
{
    public LanguageIdentity(
        string id,
        string displayName,
        IReadOnlyList<string> fileExtensions,
        bool isCaseSensitive = true)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A language needs an id.", nameof(id));

        Id = id;
        DisplayName = displayName;

        // Extensions are matched against file paths, so they are normalised
        // once here rather than at every lookup.
        FileExtensions = fileExtensions
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct()
            .ToList();

        IsCaseSensitive = isCaseSensitive;
    }

    /// <summary>Stable identifier, used in settings and project files.</summary>
    public string Id { get; }

    /// <summary>Name shown to the user.</summary>
    public string DisplayName { get; }

    /// <summary>Extensions this language claims, each including the dot.</summary>
    public IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Whether identifiers differ by case.
    ///
    /// Visual Basic and the BASIC dialects do not, which changes how completion
    /// filters and how casing is corrected.
    /// </summary>
    public bool IsCaseSensitive { get; }

    public bool Matches(string filePath) =>
        FileExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());

    public override string ToString() => DisplayName;
}
