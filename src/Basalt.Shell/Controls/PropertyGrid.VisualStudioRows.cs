using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

public sealed partial class PropertyGrid
{
    private void AddVisualStudioRow(DesignableProperty property)
    {
        var labelText = new TextBlock
        {
            Text = property.DisplayName ?? property.Name,
            Foreground = property.IsReadOnly ? VisualStudioPalette.ReadOnlyForegroundBrush : VisualStudioPalette.ForegroundBrush,
            FontSize = WinFormsPropertyGridPalette.FontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4 + property.Depth * 12, 0, 2, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            // Long names such as AccessibleDefaultActionDescription are cut by the column.
            [ToolTip.TipProperty] = property.DisplayName ?? property.Name,
        };
        var labelContent = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        labelContent.Children.Add(labelText);
        Grid.SetColumn(labelText, 1);
        if (property.IsExpandable)
        {
            var expand = new Button
            {
                Content = _expandedProperties.Contains(property.Name) ? "−" : "+",
                Width = 15,
                Height = 18,
                MinHeight = 0,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                FontSize = WinFormsPropertyGridPalette.FontSize,
            };
            VisualStudioPalette.StyleButton(expand, Brushes.Transparent);
            expand.Click += (_, _) => ToggleVisualStudioExpansion(property);
            labelContent.Children.Add(expand);
        }
        // A dashed rectangle in the selection text colour: the selection fill
        // alone cannot tell a keyboard-focused row from one that merely stays selected.
        var focusMark = new Avalonia.Controls.Shapes.Rectangle
        {
            Stroke = VisualStudioPalette.SelectionForegroundBrush,
            StrokeThickness = 1,
            StrokeDashArray = [1, 2],
            Margin = new Thickness(1),
            IsHitTestVisible = false,
            IsVisible = false,
        };
        Grid.SetColumnSpan(focusMark, 2);
        labelContent.Children.Add(focusMark);
        var label = new Border
        {
            Child = labelContent,
            Background = Brushes.Transparent
        };
        var editor = BuildVisualStudioEditor(property);
        var separator = new GridSplitter
        {
            Width = 3,
            // The theme's panel border: visible, never louder than the text.
            Background = VisualStudioPalette.GridLineBrush,
            ResizeDirection = GridResizeDirection.Columns,
        };
        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(_nameWidth) { MinWidth = 55 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Children = { label, separator, editor },
        };
        Grid.SetColumn(separator, 1);
        Grid.SetColumn(editor, 2);
        var row = new Border
        {
            Name = "PropertyGridRow",
            Height = WinFormsPropertyGridPalette.RowHeight,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = VisualStudioPalette.GridLineBrush,
            Child = columns,
            Focusable = true,
            Background = VisualStudioPalette.BackgroundBrush,
        };
        var entry = new VisualStudioEntry(property, row, label, labelText, editor, focusMark);
        _visualStudioEntries.Add(entry);
        columns.ColumnDefinitions[0].PropertyChanged += (_, change) =>
        {
            if (change.Property.Name != nameof(ColumnDefinition.Width))
                return;
            _nameWidth = columns.ColumnDefinitions[0].Width;
            foreach (var other in _visualStudioEntries)
                if (other.Row.Child is Grid grid && !ReferenceEquals(grid, columns))
                    grid.ColumnDefinitions[0].Width = _nameWidth;
        };
        row.PointerPressed += (_, _) => SelectVisualStudioProperty(entry, false);
        label.PointerPressed += (_, _) => SelectVisualStudioProperty(entry, true);
        row.GotFocus += (_, _) => SelectVisualStudioProperty(entry, false);
        row.KeyDown += (_, args) => HandleVisualStudioRowKey(entry, args);
        var reset = new MenuItem
        {
            Header = Localizer.Get(StringKeys.PropertiesReset),
            IsEnabled = property.CanReset
        };
        reset.Click += (_, _) => CommitVisualStudioProperty(property, null);
        row.ContextMenu = new ContextMenu { ItemsSource = new[] { reset } };
        _visualStudioRows.Children.Add(row);
    }

    private static string VisualStudioPropertyKey(DesignableProperty property) =>
        property.ParentName is null ? property.Name : property.ParentName + "." + property.Name;

    private bool _restoringPropertyFocus;

    internal bool FocusSelectedProperty()
    {
        var entry = _visualStudioEntries.FirstOrDefault(item =>
            VisualStudioPropertyKey(item.Property) == _visualStudioSelectedName) ?? _visualStudioEntries.FirstOrDefault();
        if (entry is null || !entry.Row.IsEffectivelyVisible)
            return false;
        _restoringPropertyFocus = true;
        try
        {
            SelectVisualStudioProperty(entry, false);
            return entry.Row.Focus();
        }
        finally
        {
            _restoringPropertyFocus = false;
        }
    }

    private void SelectVisualStudioProperty(VisualStudioEntry entry, bool focus)
    {
        var propertyKey = VisualStudioPropertyKey(entry.Property);
        var changedSelection = _visualStudioSelectedName != propertyKey;
        _visualStudioSelectedName = propertyKey;
        ApplyVisualStudioSelection();
        _visualStudioName.Text = entry.Property.DisplayName ?? entry.Property.Name;
        _visualStudioDescription.Text = entry.Property.Description ?? "";
        if (focus)
        {
            entry.Row.Focus();
        }
        // Window activation can refocus the previously selected row after a modal
        // editor closes. That focus event must not move a manually scrolled viewport.
        if (!_restoringPropertyFocus && (changedSelection || focus))
        {
            entry.Row.BringIntoView();
        }
    }

    private void ToggleVisualStudioExpansion(DesignableProperty property)
    {
        var expanded = _expandedProperties.Add(property.Name);
        if (!expanded) _expandedProperties.Remove(property.Name);
        PropertyExpansionRequested?.Invoke(property, expanded);
        Rebuild();
    }
}
