using System.Diagnostics;
using Basalt.Core.Localization;

namespace Basalt.Workspace;

/// <summary>
/// The "dotnet" processes the IDE starts for builds, runs and tests.
///
/// MSBuild, the compilers and the CLI speak the operating system's language
/// unless told otherwise, so on an Italian machine an English IDE listed
/// "'y' non è dichiarato" in its Problems panel and "Compilazione riuscita"
/// in the Output panel. <c>DOTNET_CLI_UI_LANGUAGE</c> makes the tools use the
/// language the IDE is displayed in.
/// </summary>
public static class DotnetProcess
{
    public const string LanguageVariable = "DOTNET_CLI_UI_LANGUAGE";

    /// <summary>A redirected, windowless "dotnet" start in the given folder.</summary>
    internal static ProcessStartInfo CreateStartInfo(string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDirectory is not null) startInfo.WorkingDirectory = workingDirectory;

        UseInterfaceLanguage(startInfo);

        return startInfo;
    }

    /// <summary>
    /// Makes the process report in the IDE's own language, unless the user
    /// already chose one: the environment a start inherits is the user's, and
    /// a value they set on purpose beats the IDE's guess.
    /// </summary>
    public static void UseInterfaceLanguage(ProcessStartInfo startInfo)
    {
        if (startInfo.Environment.TryGetValue(LanguageVariable, out var chosen)
            && !string.IsNullOrWhiteSpace(chosen)) return;

        startInfo.Environment[LanguageVariable] = Localizer.InterfaceLanguage;
    }
}
