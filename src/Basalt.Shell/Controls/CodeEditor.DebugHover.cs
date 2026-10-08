namespace Basalt.Shell.Controls;

public sealed partial class CodeEditor
{
    /// <summary>
    /// Whether resting the pointer should leave the symbol tooltip alone because the debugger's
    /// data tip answers instead. Visual Studio shows only the value while stopped: two popups over
    /// the same word, one describing the declaration and one the value, cover each other.
    /// </summary>
    public Func<bool>? HoverYieldsToDebugger { get; set; }

    /// <summary>
    /// The text editor the data tip listens to. Handed out directly because the window connects the
    /// debugger while building the tab, before this control is in the visual tree, so a visual-tree
    /// search would find nothing and the data tip would never be wired.
    /// </summary>
    internal AvaloniaEdit.TextEditor TextEditorForDataTips => _editor;
}
