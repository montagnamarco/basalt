using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>
/// Where an assistant's API key is read, written and removed.
///
/// The window and the Settings page depend on this rather than on the platform
/// store, so a test can hand them an in-memory store and nothing it does reaches
/// the OS keychain or the key file. <see cref="ApiKeyStore"/> is the real one.
/// </summary>
public interface IApiKeyStore
{
    /// <summary>Whether the platform keychain is being used rather than a file.</summary>
    bool UsesPlatformKeychain { get; }

    /// <summary>The key for an assistant, or null when none is set.</summary>
    string? Get(AiVendor vendor);

    /// <summary>Stores a key, replacing any already there.</summary>
    bool Set(AiVendor vendor, string apiKey);

    bool Remove(AiVendor vendor);

    bool Has(AiVendor vendor);
}
