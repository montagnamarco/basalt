using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Basalt.Shell.Controls;

public sealed partial class PropertyGrid
{
    private void UpdateVisualStudioThemeChrome()
    {
        var palette = VisualStudioPalette;
        _visualStudioLayout.Background = palette.BackgroundBrush;
        _visualStudioScroll.Background = palette.BackgroundBrush;
        _visualStudioName.Foreground = palette.ForegroundBrush;
        _visualStudioDescription.Foreground = palette.ReadOnlyForegroundBrush;
        _visualStudioDescriptionPane.BorderBrush = palette.GridLineBrush;
        _visualStudioDescriptionPane.Background = palette.CategoryBackgroundBrush;

        // The toggle's checked and hovered fills come from Fluent's template, so
        // they are overridden with styles carrying the theme colours.
        // They depend on the palette alone, so they are written when it changes
        // and not on every toggle.
        palette.InstallOn(_visualStudioLayout);
        if (!ReferenceEquals(_toolbarStylesFor, palette))
        {
            _toolbarStylesFor = palette;
            _visualStudioToolbar.Styles.Clear();
            AddToolbarStyle([":checked"], palette.SelectionBackgroundBrush);
            AddToolbarStyle([":pointerover"], palette.InactiveSelectionBackgroundBrush);
            AddToolbarStyle([":pressed"], palette.InactiveSelectionBackgroundBrush);
            // After the single-state ones, and more specific, so a checked button
            // keeps its checked fill under the pointer.
            AddToolbarStyle([":checked", ":pointerover"], palette.SelectionBackgroundBrush);
            AddToolbarStyle([":checked", ":pressed"], palette.SelectionBackgroundBrush);
        }

        foreach (var button in _visualStudioToolbar.Children.OfType<ToggleButton>())
        {
            var checkedState = button.IsChecked == true;
            button.Background = Brushes.Transparent;
            button.Foreground = checkedState ? palette.SelectionForegroundBrush : palette.ForegroundBrush;
            if (button.Content is ShapePath icon)
                icon.Stroke = button.Foreground;
        }
    }

    private WinFormsPropertyGridPalette? _toolbarStylesFor;

    /// <summary>
    /// One pseudo-class per state: <c>.Class(":checked:pointerover")</c> names a
    /// single class that no control ever has, so the combined states never matched.
    /// </summary>
    private void AddToolbarStyle(string[] states, IBrush background)
    {
        var style = new Style(selector =>
        {
            var button = selector.OfType<ToggleButton>();
            foreach (var state in states) button = button.Class(state);
            return button.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
        });
        style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, background));
        _visualStudioToolbar.Styles.Add(style);
    }

    /// <summary>
    /// Colours the selected row: the selection colour while the grid has keyboard
    /// focus, the quieter inactive one otherwise, plus a dashed focus mark so the
    /// focused row is visible in every theme.
    /// </summary>
    private void ApplyVisualStudioSelection()
    {
        var palette = VisualStudioPalette;
        var focused = IsKeyboardFocusWithin;
        foreach (var entry in _visualStudioEntries)
        {
            var selected = VisualStudioPropertyKey(entry.Property) == _visualStudioSelectedName;
            entry.Label.Background = !selected ? Brushes.Transparent
                : focused ? palette.SelectionBackgroundBrush : palette.InactiveSelectionBackgroundBrush;
            entry.LabelText.Foreground = selected && focused ? palette.SelectionForegroundBrush
                : entry.Property.IsReadOnly ? palette.ReadOnlyForegroundBrush : palette.ForegroundBrush;
            entry.FocusMark.IsVisible = selected && focused;
        }
    }

    private void OnVisualStudioThemeChanged()
    {
        var fresh = WinFormsPropertyGridPalette.For(this);
        if (_visualStudioPalette is not null && _visualStudioPalette.SameAs(fresh))
            return;
        _visualStudioPalette = fresh;
        RefreshVisualStudioTheme();
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
