using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public Task ApplyFieldRenameAsync(string renamedSource, string newName, Action commit, Action undo, Action redo) =>
        ApplyAsync(_ => RenamedDesignerSource(renamedSource, newName), commit, undo, redo);

    internal static string RenamedDesignerSource(string source, string newName)
    {
        var parsed = new WinFormsDesignerDocument(source);
        return parsed.Controls.Any(control => control.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
            ? parsed.SetProperty(newName, "Name", WinFormsDesignerValues.Serialize(newName)) : parsed.Source;
    }
}
