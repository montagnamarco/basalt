namespace Basalt.Workspace.Ai;

/// <summary>
/// Keeps the assistant's tools inside the opened solution folder.
///
/// A path in a tool call is text the model wrote, possibly after reading a
/// file that tells it what to ask for. Three things are refused: a path that
/// leaves the solution folder (<c>..</c>, an absolute path elsewhere), one
/// that gets out through a symbolic link, and a file that holds secrets,
/// wherever in the folder it sits.
/// </summary>
public sealed class AiWorkspaceSandbox
{
    private readonly StringComparison _comparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Directory names never listed or searched: build output, VCS internals, packages.</summary>
    public static IReadOnlyList<string> ExcludedDirectories { get; } =
        ["bin", "obj", ".git", ".vs", "node_modules", ".idea", ".vscode"];

    public AiWorkspaceSandbox(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("A solution folder is required.", nameof(rootDirectory));

        Root = RealPath(Path.GetFullPath(rootDirectory));
    }

    /// <summary>The solution folder, with every symbolic link in its path resolved.</summary>
    public string Root { get; }

    /// <summary>
    /// The full path of a file the assistant may read or propose changes to.
    /// </summary>
    /// <exception cref="AiToolException">The path is outside the folder or names a secret.</exception>
    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
            throw new AiToolException("The path is empty or invalid.");

        // A secret is refused by its spelling first, even when the file does
        // not exist, and the path is checked against the folder only after
        // links are followed: the folder may itself be spelled through a link
        // (/var is /private/var on macOS) and the caller may use either.
        var lexical = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));

        if (IsSecret(lexical))
            throw new AiToolException($"'{path}' may hold secrets and is not available to the assistant.");

        // And again after links are followed: a link inside the folder that
        // points out of it, or at a secret, is still an escape.
        var real = RealPath(lexical);

        if (!IsInside(real, Root))
            throw new AiToolException($"'{path}' leads outside the solution folder.");

        if (IsSecret(real))
            throw new AiToolException($"'{path}' may hold secrets and is not available to the assistant.");

        return real;
    }

    /// <summary>The path as the assistant sees it: relative to the folder, with forward slashes.</summary>
    public string Relative(string fullPath) =>
        Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    /// <summary>
    /// Whether a path holds credentials or the means to get them, by name and
    /// by where it lives: key and certificate files, environment files, tool
    /// credentials, publish profiles, per-developer settings, and everything
    /// under .git (its config can carry tokens).
    /// </summary>
    public static bool IsSecret(string fullPath)
    {
        var name = Path.GetFileName(fullPath);

        if (name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) return true;

        string[] secretExtensions =
            [".pem", ".key", ".pfx", ".p12", ".jks", ".keystore", ".ppk", ".kdbx", ".gpg", ".asc",
             ".snk", ".publishsettings", ".tfvars", ".tfstate"];

        if (secretExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) return true;

        if (name.EndsWith(".pubxml.user", StringComparison.OrdinalIgnoreCase)) return true;

        string[] secretNames =
            [".npmrc", ".netrc", ".pypirc", ".pgpass", ".git-credentials", "credentials", "secrets.json",
             "nuget.config", "launchsettings.json", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519"];

        if (secretNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;

        if (name.StartsWith("id_rsa", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("id_ed25519", StringComparison.OrdinalIgnoreCase)) return true;

        // appsettings.Development.json, appsettings.Production.json and the
        // like are where connection strings go; only the plain one is shared.
        if (name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)) return true;

        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string[] secretDirectories = [".ssh", ".aws", ".gnupg", ".basalt", ".azure", ".kube", ".git"];

        if (parts.Any(p => secretDirectories.Contains(p, StringComparer.OrdinalIgnoreCase))) return true;

        // The IDE's own key store, wherever the home directory is.
        var keys = Path.GetFullPath(ApiKeyStore.DefaultFallbackPath);

        return fullPath.StartsWith(keys, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// A file the assistant may propose to write: readable, and not build
    /// output or version-control internals, which are never edited by hand.
    /// </summary>
    public string ResolveForWrite(string path)
    {
        var full = Resolve(path);

        var parts = Relative(full).Split('/');

        if (parts[..^1].Any(p => ExcludedDirectories.Contains(p, StringComparer.OrdinalIgnoreCase)))
            throw new AiToolException($"'{path}' is build output or repository data and is not edited.");

        return full;
    }

    private bool IsInside(string path, string root)
    {
        if (path.Equals(root, _comparison)) return true;

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, _comparison);
    }

    /// <summary>The path in its one canonical spelling; see <see cref="PathIdentity"/>.</summary>
    internal static string RealPath(string fullPath)
    {
        try
        {
            return PathIdentity.Canonical(fullPath);
        }
        catch (InvalidOperationException ex)
        {
            throw new AiToolException(ex.Message);
        }
    }
}
