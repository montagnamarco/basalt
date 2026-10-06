using Basalt.Core.Localization;
using Basalt.Workspace.Publishing;

namespace Basalt.Shell;

public partial class MainWindow
{
    /// <summary>
    /// "File > Make executable": asks for platforms and a folder, publishes,
    /// and shows the result in the file manager when everything was made.
    /// </summary>
    private void RunMakeExecutable() =>
        Guarded.Run(ShowMakeExecutableAsync, ViewModel.WriteOutput, "make");

    private async Task ShowMakeExecutableAsync()
    {
        // One publish at a time, and not while a build is writing the same obj.
        if (ViewModel.IsBusy)
        {
            ViewModel.StatusMessage = Localizer.Get(StringKeys.StatusMakeExeBusy);
            return;
        }

        if (ViewModel.StartupProject is not { } project)
        {
            ViewModel.StatusMessage = Localizer.Get(StringKeys.StatusNoProject);
            return;
        }

        var remembered = ViewModel.MakeExecutableChoice;
        var dialog = new MakeExecutableDialog(
            ViewModel.MakeExecutableProjectName,
            remembered?.Rids ?? [],
            remembered?.OutputRoot ?? PublishRequest.DefaultOutputRoot(project));

        if (await dialog.ShowDialog<MakeExecutableChoiceResult?>(this) is not { } choice) return;

        var artifact = await ViewModel.MakeExecutableAsync(
            new PublishRequest(project, choice.Targets, choice.OutputRoot));

        if (artifact is not null) RevealInFileManager(artifact);
    }
}
