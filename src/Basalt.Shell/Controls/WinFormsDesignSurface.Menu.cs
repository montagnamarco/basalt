using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormsControl = System.Windows.Forms.Control;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsDesignSurface
{
    private WinFormsMenuDesignEditor? _inlineMenuEditor;
    private FormsControl? _inlineMenuControl;

    internal void ShowInlineMenuEditor(WinFormsMenuDesignEditor? editor, FormsControl? menu)
    {
        if (_inlineMenuEditor is not null)
        {
            _host.Children.Remove(_inlineMenuEditor);
        }

        _inlineMenuEditor = editor;
        _inlineMenuControl = menu;
        if (editor is null || menu is null || !BelongsToRoot(menu))
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
