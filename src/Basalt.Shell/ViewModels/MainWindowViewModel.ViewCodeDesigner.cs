using CommunityToolkit.Mvvm.Input;

namespace Basalt.Shell.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// F7: shows the handwritten code behind the active designer - the same
    /// ".Designer.vb"/".vb" pair the Solution Explorer's "View Code" command
    /// already opens. A no-op outside a Designer tab: there is nothing above
    /// the code for F7 to show.
    /// </summary>
    [RelayCommand]
    private Task ViewCodeAsync()
    {
        if (ActiveDocument is not { } document ||
            !document.FilePath.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var companion = document.FilePath[..^".Designer.vb".Length] + ".vb";
        return File.Exists(companion) ? OpenFileAsync(companion, inDesigner: false) : Task.CompletedTask;
    }

    /// <summary>
    /// Shift+F7: shows the designer for the active code-behind, the companion gesture to
    /// <see cref="ViewCodeAsync"/>. A no-op for a code file with no ".Designer.vb" of its
    /// own - an ordinary module or class, say - rather than guessing at one.
    /// </summary>
    [RelayCommand]
    private Task ViewDesignerAsync()
    {
        if (ActiveDocument is not { } document ||
            document.FilePath.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase) ||
            !document.FilePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var designer = document.FilePath[..^".vb".Length] + ".Designer.vb";
        return File.Exists(designer) ? OpenFileAsync(designer, inDesigner: true) : Task.CompletedTask;
    }
}
