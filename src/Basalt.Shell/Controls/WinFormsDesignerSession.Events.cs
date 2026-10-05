namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public Task ApplyCompanionEditAsync(Action commit, Action undo, Action redo) =>
        ApplyAsync(document => document.Source, commit, undo, redo, commitUnchangedSource: true);
}
