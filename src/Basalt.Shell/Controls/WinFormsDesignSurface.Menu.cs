using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormsControl = System.Windows.Forms.Control;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsDesignSurface
{
    // Both the MenuStrip and the ToolStrip/StatusStrip "Type Here" editors are a single
    // overlay hosted above whichever strip is selected: only one can be open at a time,
    // same as only one control can be selected, so the two callers share one slot rather
    // than duplicating the hosting, positioning and hit-testing logic below.
    private Control? _inlineMenuEditor;
    private FormsControl? _inlineMenuControl;

    internal void ShowInlineMenuEditor(WinFormsMenuDesignEditor? editor, FormsControl? menu) =>
        ShowInlineOverlay(editor, menu);

    internal void ShowInlineToolStripEditor(WinFormsToolStripDesignEditor? editor, FormsControl? strip) =>
        ShowInlineOverlay(editor, strip);

    private void ShowInlineOverlay(Control? editor, FormsControl? target)
    {
        if (_inlineMenuEditor is not null)
        {
            _host.Children.Remove(_inlineMenuEditor);
        }

        _inlineMenuEditor = editor;
        _inlineMenuControl = target;
        if (editor is null || target is null || !BelongsToRoot(target))
        {
            return;
        }

        PositionInlineMenuEditor();
        _host.Children.Add(editor);
    }

    private void PositionInlineMenuEditor()
    {
        if (_inlineMenuEditor is null || _inlineMenuControl is null ||
            !BelongsToRoot(_inlineMenuControl))
        {
            return;
        }

        var bounds = BoundsOnHost(_inlineMenuControl);
        Canvas.SetLeft(_inlineMenuEditor, bounds.X + 1);
        Canvas.SetTop(_inlineMenuEditor, bounds.Y + 1);
    }

    private bool IsInlineMenuInput(object? source)
    {
        if (_inlineMenuEditor is null || source is not Visual current)
        {
            return false;
        }

        for (Visual? visual = current; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, _inlineMenuEditor))
            {
                return true;
            }
        }
        return false;
    }
}
