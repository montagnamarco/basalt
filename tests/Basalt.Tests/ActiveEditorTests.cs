using Avalonia.Headless.XUnit;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Which editor a refactoring acts on.
///
/// It used to be whichever came first out of the content dictionary. With
/// more than one file open that is not the one being worked in, so every
/// refactoring read the caret from the wrong document and then did nothing,
/// or something surprising, without saying why.
/// </summary>
public class ActiveEditorTests
{
    private static string WriteFile(string root, string name, string text)
    {
        var path = Path.Combine(root, name);

        File.WriteAllText(path, text);

        return path;
    }

    [AvaloniaFact]
    public async Task PicksTheEditorOfTheDocumentBeingWorkedIn()
    {
        var root = Directory.CreateTempSubdirectory("basalt-activeeditor-").FullName;

        try
        {
            var first = WriteFile(root, "First.vb", "Module First\nEnd Module\n");
            var second = WriteFile(root, "Second.vb", "Module Second\nEnd Module\n");

            using var window = new TestWindow();

            var vm = (MainWindowViewModel)window.Window.DataContext!;

            await vm.OpenFileAsync(first);
            await vm.OpenFileAsync(second);

            await window.SettleAsync();

            // Second.vb was opened last, so it is the active one.
            Assert.Equal(second, vm.ActiveDocument?.FilePath);

            var editor = window.Window.CurrentEditorForTests();

            Assert.NotNull(editor);
            Assert.Contains("Second", editor.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Module First", editor.Text, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task FollowsTheActiveDocumentWhenItChanges()
    {
        var root = Directory.CreateTempSubdirectory("basalt-activeeditor-").FullName;

        try
        {
            var first = WriteFile(root, "First.vb", "Module First\nEnd Module\n");
            var second = WriteFile(root, "Second.vb", "Module Second\nEnd Module\n");

            using var window = new TestWindow();

            var vm = (MainWindowViewModel)window.Window.DataContext!;

            await vm.OpenFileAsync(first);
            await vm.OpenFileAsync(second);

            await window.SettleAsync();

            // Back to the first: the refactoring has to follow.
            await vm.OpenFileAsync(first);

            await window.SettleAsync();

            var editor = window.Window.CurrentEditorForTests();

            Assert.NotNull(editor);
            Assert.Contains("Module First", editor.Text, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task ReadsTheCaretFromTheActiveDocument()
    {
        // What every refactoring does first: take the word under the caret.
        // Reading it from the wrong editor is how "Rename" ended up renaming
        // nothing, with no message to say why.
        var root = Directory.CreateTempSubdirectory("basalt-activeeditor-").FullName;

        try
        {
            var first = WriteFile(root, "First.vb", "Module Alpha\nEnd Module\n");
            var second = WriteFile(root, "Second.vb", "Module Beta\nEnd Module\n");

            using var window = new TestWindow();

            var vm = (MainWindowViewModel)window.Window.DataContext!;

            await vm.OpenFileAsync(first);
            await vm.OpenFileAsync(second);

            await window.SettleAsync();

            var editor = window.Window.CurrentEditorForTests();

            Assert.NotNull(editor);

            // On "Beta" in the active document.
            editor.CaretOffset = editor.Text.IndexOf("Beta", StringComparison.Ordinal) + 2;

            var word = editor.Text
                .Substring(editor.CaretOffset - 2, 4);

            Assert.Equal("Beta", word);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task FindsTheOnlyEditorWhenOneFileIsOpen()
    {
        var root = Directory.CreateTempSubdirectory("basalt-activeeditor-").FullName;

        try
        {
            var only = WriteFile(root, "Only.vb", "Module Only\nEnd Module\n");

            using var window = new TestWindow();

            var vm = (MainWindowViewModel)window.Window.DataContext!;

            await vm.OpenFileAsync(only);

            await window.SettleAsync();

            Assert.NotNull(window.Window.CurrentEditorForTests());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
