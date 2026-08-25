namespace Basalt.Extensibility;

/// <summary>
/// Holds the languages the IDE knows about and resolves one from a file path.
///
/// Registration is by instance rather than by discovery: a language that ships
/// with the IDE and one loaded from elsewhere register the same way, so nothing
/// in the IDE needs to know which is which.
/// </summary>
public sealed class LanguageRegistry
{
    private readonly List<ILanguageProvider> _providers = [];
    private readonly Lock _gate = new();

    /// <summary>Raised when the set of languages changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<ILanguageProvider> Providers
    {
        get { lock (_gate) return _providers.ToList(); }
    }

    public void Register(ILanguageProvider provider)
    {
        lock (_gate)
        {
            // Re-registering an id replaces the previous provider, so a
            // language can be updated without restarting.
            _providers.RemoveAll(p => p.Identity.Id == provider.Identity.Id);
            _providers.Add(provider);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Unregister(string languageId)
    {
        bool removed;
        lock (_gate) removed = _providers.RemoveAll(p => p.Identity.Id == languageId) > 0;

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <summary>The language claiming a file, or null when none does.</summary>
    public ILanguageProvider? ForFile(string filePath)
    {
        lock (_gate) return _providers.FirstOrDefault(p => p.Identity.Matches(filePath));
    }

    public ILanguageProvider? ById(string languageId)
    {
        lock (_gate)
            return _providers.FirstOrDefault(p =>
                string.Equals(p.Identity.Id, languageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every extension any registered language claims.</summary>
    public IReadOnlyList<string> KnownExtensions
    {
        get
        {
            lock (_gate)
                return _providers
                    .SelectMany(p => p.Identity.FileExtensions)
                    .Distinct()
                    .OrderBy(e => e)
                    .ToList();
        }
    }

    /// <summary>Tells every language which solution is open.</summary>
    public async Task OpenSolutionAsync(string solutionOrProjectPath, CancellationToken ct = default)
    {
        foreach (var provider in Providers)
        {
            try
            {
                await provider.OpenSolutionAsync(solutionOrProjectPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One language failing to load a solution must not stop the
                // others: a file it cannot handle is still editable.
                Failed?.Invoke(this, new LanguageFailure(provider.Identity, ex));
            }
        }
    }

    /// <summary>Raised when a language throws while doing its work.</summary>
    public event EventHandler<LanguageFailure>? Failed;
}

/// <summary>A language provider that failed, and why.</summary>
public sealed record LanguageFailure(LanguageIdentity Language, Exception Error);
