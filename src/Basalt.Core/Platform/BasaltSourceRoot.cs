namespace Basalt.Core.Platform;

/// <summary>
/// Finds the folder that holds the Basalt.Forms source projects (the repository's
/// <c>src</c> folder).
///
/// Two features need it and must agree on the answer: the WinForms solution
/// template, which writes project references to it, and the Windows desktop
/// compatibility layer, which retargets Visual Studio projects onto it. One
/// lookup keeps them from pointing at different installations.
/// </summary>
public static class BasaltSourceRoot
{
    /// <summary>Environment variable that overrides the lookup.</summary>
    public const string EnvironmentVariable = "BASALT_FORMS_PATH";

    /// <summary>
    /// The configured folder, or the nearest <c>src</c> folder above the running
    /// application that contains <c>Basalt.Forms/Basalt.Forms.csproj</c>; null
    /// when neither exists (an IDE installed without its sources).
    /// </summary>
    public static string? TryFind()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "src", "Basalt.Forms", "Basalt.Forms.csproj")))
                return Path.Combine(directory, "src");
            directory = Path.GetDirectoryName(directory);
        }
        return null;
    }

    /// <summary>Like <see cref="TryFind"/>, but explains how to fix a failure.</summary>
    public static string Find() =>
        TryFind() ?? throw new DirectoryNotFoundException(
            $"Set {EnvironmentVariable} to the folder containing the Basalt.Forms source project.");
}
