using Basalt.Core.Localization;
using Basalt.Core.Platform;
using Basalt.Workspace;

namespace Basalt.Shell.ViewModels;

/// <summary>
/// The Visual Studio Windows desktop retargeting as the window sees it: one
/// session shared by every service that touches the projects.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// What Windows desktop projects of the opened solution are retargeted
    /// with. One session for Roslyn, restore, build, clean, run and tests, so
    /// they cannot disagree about where the output is or which framework it is.
    /// </summary>
    internal WindowsDesktopCompatibilitySession Compatibility { get; } = new();

    private void ShareCompatibilitySession()
    {
        _languageService.Compatibility = Compatibility;
        _buildService.Compatibility = Compatibility;
        _runService.Compatibility = Compatibility;
    }

    /// <summary>The sessions the VM's own services use. For tests.</summary>
    internal IReadOnlyList<WindowsDesktopCompatibilitySession> ServiceSessionsForTests =>
        [_languageService.Compatibility, _buildService.Compatibility, _runService.Compatibility];

    /// <summary>
    /// Gives a "dotnet" command the properties the build used: without them a
    /// Visual Studio Windows desktop project is cleaned as the original
    /// framework (it fails off Windows, which made Rebuild fail) and
    /// bin/basalt is left behind.
    /// </summary>
    private async Task AddCompatibilityArgumentsAsync(System.Diagnostics.ProcessStartInfo start)
    {
        if (StartupProject is null) return;
        foreach (var argument in await Compatibility.GetArgumentsAsync(StartupProject).ConfigureAwait(true))
            start.ArgumentList.Add(argument);
    }

    /// <summary>
    /// Explains in the Output panel why a Windows Forms project will not build,
    /// when the Basalt.Forms sources it would be retargeted onto are missing.
    /// Without it the user meets the SDK's NETSDK1100 with no hint of the cause.
    /// </summary>
    private async Task ReportMissingWindowsDesktopSourcesAsync(string path)
    {
        var retargeting = await Compatibility.GetAsync(path).ConfigureAwait(true);
        if (retargeting.SourcesMissing) AppendOutput(MissingWindowsDesktopSourcesMessage);
    }

    private static string MissingWindowsDesktopSourcesMessage =>
        Localizer.Get(StringKeys.BuildWindowsDesktopSourcesMissing, BasaltSourceRoot.EnvironmentVariable);
}
