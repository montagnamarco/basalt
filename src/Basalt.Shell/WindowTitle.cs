namespace Basalt.Shell;

/// <summary>
/// Computes the main window's title the way Visual Basic 6 and Visual
/// Studio do.
///
/// "Basalt" alone while nothing is open; the solution's name once one is,
/// with a bracketed state that follows the debugger: "[design]" normally,
/// "[run]" while the program runs (debugged or not) and "[break]" while
/// paused in the debugger. A pure function rather than a property on the
/// window, so the four states can be proved without building a window.
/// </summary>
public static class WindowTitle
{
    public const string ApplicationName = "Basalt";

    public static string For(string? solutionPath, bool isRunning, bool isPaused)
    {
        if (string.IsNullOrEmpty(solutionPath)) return ApplicationName;

        var name = Path.GetFileNameWithoutExtension(solutionPath);
        var state = isPaused ? "break" : isRunning ? "run" : "design";

        return $"{name} - {ApplicationName} [{state}]";
    }
}
