using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.VisualTree;

namespace Basalt.Shell.Controls;

public sealed partial class PropertyGrid
{
    private void UpdateVisualStudioThemeChrome()
    {
        var palette = VisualStudioPalette;
        _visualStudioLayout.Background = palette.BackgroundBrush;
        _visualStudioName.Foreground = palette.ForegroundBrush;
        _visualStudioDescription.Foreground = palette.ForegroundBrush;
        _visualStudioDescriptionPane.BorderBrush = palette.GridLineBrush;

        foreach (var button in _visualStudioToolbar.Children.OfType<ToggleButton>())
        {
            button.Foreground = palette.ForegroundBrush;
            if (button.Content is ShapePath icon)
                icon.Stroke = palette.ForegroundBrush;
        }
    }

    private void RefreshVisualStudioTheme()
    {
        if (!VisualStudioStyle)
        {
            UpdateVisualStudioThemeChrome();
            return;
        }

        var drafts = new Dictionary<string, string?>();
        string? focusedProperty = null;
        foreach (var entry in _visualStudioEntries)
        {
            var editor = FindVisualStudioTextEditor(entry.Editor);
            if (editor is not null)
                drafts[VisualStudioPropertyKey(entry.Property)] = editor.Text;
            if (entry.Row.IsKeyboardFocusWithin)
                focusedProperty = VisualStudioPropertyKey(entry.Property);
        }

        RebuildVisualStudioGrid();

        foreach (var entry in _visualStudioEntries)
        {
            var key = VisualStudioPropertyKey(entry.Property);
            var editor = FindVisualStudioTextEditor(entry.Editor);
            if (editor is not null && drafts.TryGetValue(key, out var draft))
                editor.Text = draft;
            if (key == focusedProperty)
            {
                if (editor is not null)
                    editor.Focus();
                else
                    entry.Row.Focus();
            }
        }
    }

    private static TextBox? FindVisualStudioTextEditor(Control editor) =>
        editor as TextBox ?? editor.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
}
