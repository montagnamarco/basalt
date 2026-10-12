using Basalt.Shell.ViewModels;

namespace Basalt.Shell;

/// <summary>
/// Where the files git names are, and what the editor shows after git
/// changes them.
/// </summary>
internal static class GitPaths
{
    /// <summary>
    /// The top of the repository a folder is in, or null outside one.
    /// </summary>
    /// <remarks>
    /// Git reports paths from that top, not from the solution's folder. In a
    /// solution kept in a subfolder of its repository, "src/App/Module1.vb"
    /// joined to the solution's folder named a file that does not exist, so
    /// Open File from Git Changes opened nothing. ".git" is a folder in a
    /// repository and a file in a worktree or a submodule; both mark the top.
    /// </remarks>
    public static string? RepositoryRootOf(string folder)
    {
        for (var current = new DirectoryInfo(folder); current is not null; current = current.Parent)
        {
            var marker = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker)) return current.FullName;
        }

        return null;
    }

    /// <summary>
    /// Puts an open document back to the text git restored on disk.
    /// </summary>
    /// <remarks>
    /// Discard Changes rewrites the file, but the tab kept the edit, marked as
    /// unsaved: saving it, as anyone would, wrote the discarded change back.
    /// The user confirmed the discard, so the tab's own edits go with it. A
    /// file git removed (an untracked one) is left to the tab as it is.
    /// </remarks>
    public static void ReloadDiscarded(IEnumerable<EditorDocumentViewModel> documents, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return;

        foreach (var document in documents)
        {
            if (!string.Equals(Path.GetFullPath(document.FilePath), fullPath, StringComparison.OrdinalIgnoreCase))
                continue;

            document.Text = File.ReadAllText(fullPath);
            document.MarkSaved();
        }
    }
}
