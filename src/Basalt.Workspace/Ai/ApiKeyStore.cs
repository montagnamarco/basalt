using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Basalt.Core.Services;

namespace Basalt.Workspace.Ai;

/// <summary>
/// Where the API keys are kept.
///
/// Not in the settings file: those are plain JSON a user may well copy between
/// machines or paste into a bug report, and an API key is a credential that
/// bills its owner. The platform's own keychain is used where there is one,
/// and a file readable only by the user where there is not.
/// </summary>
public sealed class ApiKeyStore
{
    private const string ServiceName = "Basalt";

    private readonly string _fallbackPath;

    public ApiKeyStore(string? fallbackPath = null) =>
        _fallbackPath = fallbackPath ?? DefaultFallbackPath;

    /// <summary>Where keys go when there is no keychain to put them in.</summary>
    public static string DefaultFallbackPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".basalt", "keys");

    /// <summary>Whether the platform keychain is being used rather than a file.</summary>
    public bool UsesPlatformKeychain => OperatingSystem.IsMacOS();

    /// <summary>The key for an assistant, or null when none is set.</summary>
    public string? Get(AiVendor vendor)
    {
        var account = AccountFor(vendor);

        if (UsesPlatformKeychain && ReadFromKeychain(account) is { Length: > 0 } stored)
            return stored;

        return ReadFromFile(account);
    }

    /// <summary>Stores a key, replacing any already there.</summary>
    public bool Set(AiVendor vendor, string apiKey)
    {
        var account = AccountFor(vendor);

        if (apiKey.Length == 0) return Remove(vendor);

        if (UsesPlatformKeychain && WriteToKeychain(account, apiKey)) return true;

        return WriteToFile(account, apiKey);
    }

    public bool Remove(AiVendor vendor)
    {
        var account = AccountFor(vendor);

        if (UsesPlatformKeychain) RunSecurity(["delete-generic-password", "-s", ServiceName, "-a", account]);

        try
        {
            var path = FilePathFor(account);

            if (File.Exists(path)) File.Delete(path);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool Has(AiVendor vendor) => Get(vendor) is { Length: > 0 };

    private static string AccountFor(AiVendor vendor) => vendor.ToString().ToLowerInvariant();

    // The macOS keychain

    private static string? ReadFromKeychain(string account)
    {
        var (exitCode, output) = RunSecurity(
            ["find-generic-password", "-s", ServiceName, "-a", account, "-w"]);

        return exitCode == 0 ? output.Trim() : null;
    }

    private static bool WriteToKeychain(string account, string apiKey)
    {
        // "-U" updates an existing entry rather than failing on it.
        var (exitCode, _) = RunSecurity(
            ["add-generic-password", "-s", ServiceName, "-a", account, "-w", apiKey, "-U"]);

        return exitCode == 0;
    }

    /// <summary>
    /// Runs the macOS "security" tool.
    ///
    /// The key is passed as an argument, which is visible to other processes
    /// on the machine for the moment the command runs. That is how the tool
    /// works; the alternative is no keychain at all.
    /// </summary>
    private static (int ExitCode, string Output) RunSecurity(IReadOnlyList<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("security")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);

            if (process is null) return (-1, "");

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return (process.ExitCode, output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or InvalidOperationException)
        {
            return (-1, "");
        }
    }

    // The fallback file

    private string FilePathFor(string account) => Path.Combine(_fallbackPath, $"{account}.key");

    private string? ReadFromFile(string account)
    {
        try
        {
            var path = FilePathFor(account);

            if (!File.Exists(path)) return null;

            return Unprotect(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or CryptographicException or FormatException)
        {
            return null;
        }
    }

    private bool WriteToFile(string account, string apiKey)
    {
        try
        {
            Directory.CreateDirectory(_fallbackPath);

            var path = FilePathFor(account);

            File.WriteAllBytes(path, Protect(apiKey));

            // Readable by its owner alone: the file is in the user's own
            // directory, but a shared machine has other users on it.
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Obscures a key before it is written.
    ///
    /// On Windows this is real protection, tied to the user's account. Nowhere
    /// else does .NET offer an equivalent, so the key is merely not stored as
    /// plain text: the file permissions are what actually guard it. This is
    /// said plainly rather than dressed up as encryption.
    /// </summary>
    private static byte[] Protect(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);

        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416
            return ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
        }

        return bytes;
    }

    private static string Unprotect(byte[] stored)
    {
        if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser));
#pragma warning restore CA1416
        }

        return Encoding.UTF8.GetString(stored);
    }
}
