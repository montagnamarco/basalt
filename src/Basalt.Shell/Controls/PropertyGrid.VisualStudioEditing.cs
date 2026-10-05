using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

public sealed partial class PropertyGrid
{
    private Control BuildVisualStudioEditor(DesignableProperty property)
    {
        Control editor;
        if (property.IsReadOnly || property.IsCollection)
        {
            editor = new TextBlock
            {
                Text = DisplayedValue(property),
                Foreground = property.IsReadOnly ? VisualStudioPalette.ReadOnlyForegroundBrush : VisualStudioPalette.ForegroundBrush,
                FontSize = WinFormsPropertyGridPalette.FontSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = property.IsSet ? FontWeight.Bold : FontWeight.Normal,
                [ToolTip.TipProperty] = DisplayedValue(property),
            };
        }
        else if (property.EditorKind is PropertyEditorKind.Boolean or PropertyEditorKind.Enumeration)
        {
            var values = property.EditorKind == PropertyEditorKind.Boolean ? new[] { "True", "False" } : property.AllowedValues;
            var displayed = DisplayedValue(property);
            if (property.EditorKind == PropertyEditorKind.Boolean && bool.TryParse(displayed, out var boolean)) displayed = boolean.ToString();
            var combo = new ComboBox
            {
                Name = "PropertyGridValue",
                ItemsSource = values,
                SelectedItem = displayed,
                MinHeight = 0,
                Height = 20,
                Padding = new Thickness(2, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = VisualStudioPalette.ForegroundBrush,
                FontSize = WinFormsPropertyGridPalette.FontSize,
                FontWeight = property.IsSet ? FontWeight.Bold : FontWeight.Normal,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            combo.SelectionChanged += (_, _) => CommitVisualStudioProperty(property, combo.SelectedItem as string);
            editor = combo;
        }
        else
        {
            var committed = DisplayedValue(property) ?? "";
            var generation = _visualStudioEditorGeneration;
            var box = new TextBox
            {
                Name = "PropertyGridValue",
                Text = committed,
                MinHeight = 0,
                Height = 20,
                Padding = new Thickness(2, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = VisualStudioPalette.ForegroundBrush,
                FontSize = WinFormsPropertyGridPalette.FontSize,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontWeight = property.IsSet ? FontWeight.Bold : FontWeight.Normal,
                PlaceholderText = property.IsMixed ? Localizer.Get(StringKeys.WinFormsDesignerMixedValues) : null,
            };
            // The box clips rather than ellipsizes, so the full value stays one hover away.
            void ShowFullValue() => ToolTip.SetTip(box, string.IsNullOrEmpty(box.Text) ? null : box.Text);
            box.TextChanged += (_, _) => ShowFullValue();
            ShowFullValue();
            void Commit()
            {
                // Rebuilding for a theme or property refresh retires the old editor.
                // Its focus loss must not write a draft into the current document.
                if (generation != _visualStudioEditorGeneration || box.Text == committed)
                    return;
                var value = box.Text;
                committed = value ?? "";
                CommitVisualStudioProperty(property, value);
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    box.Text = committed;
                    args.Handled = true;
                }
                else if (args.Key == Key.Enter)
                {
                    Commit();
                    args.Handled = true;
                }
            };
            editor = box;
        }
        if (property.IsReadOnly || CanRequestPropertyEditor?.Invoke(property) != true) return editor;
        var ellipsis = new Button
        {
            Name = "PropertyGridEllipsis",
            Content = "…",
            Width = 20,
            Height = 20,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            [ToolTip.TipProperty] = Localizer.Get(StringKeys.PropertyGridOpenEditor),
        };
        VisualStudioPalette.StyleButton(ellipsis, VisualStudioPalette.ButtonBackgroundBrush,
            VisualStudioPalette.ButtonForegroundBrush);
        ellipsis.Click += (_, _) => PropertyEditorRequested?.Invoke(property);
        var container = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { editor, ellipsis }
        };
        Grid.SetColumn(ellipsis, 1);
        return container;
    }

    private void CommitVisualStudioProperty(DesignableProperty property, string? value)
    {
        if (value is null)
        {
            if (!property.CanReset) return;
        }
        else if (property.IsReadOnly || property.IsCollection) return;
        if (value is not null && IsNoOpEdit(property, value)) return;
        if (property.ParentName is not null) NestedPropertyEdited?.Invoke(property, value);
        else PropertyEdited?.Invoke(property.Name, value);
    }

    private void HandleVisualStudioRowKey(VisualStudioEntry entry, KeyEventArgs args)
    {
        if (args.Handled) return;
        var combo = entry.Editor as ComboBox ?? entry.Editor.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
        if (combo?.IsDropDownOpen == true && args.Key is Key.Up or Key.Down) return;
        if (args.Key is Key.Up or Key.Down)
        {
            var index = _visualStudioEntries.IndexOf(entry);
            var next = Math.Clamp(index + (args.Key == Key.Down ? 1 : -1), 0, _visualStudioEntries.Count - 1);
            SelectVisualStudioProperty(_visualStudioEntries[next], true);
            args.Handled = true;
        }
        else if (args.Key is Key.Enter or Key.F4)
        {
            if (combo is not null)
            {
                combo.Focus();
                combo.IsDropDownOpen = true;
            }
            else if (args.Key == Key.F4 && CanRequestPropertyEditor?.Invoke(entry.Property) == true)
                PropertyEditorRequested?.Invoke(entry.Property);
            else
            {
                var box = entry.Editor as TextBox ?? entry.Editor.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                box?.Focus();
                box?.SelectAll();
            }
            args.Handled = true;
        }
        else if (args.Key is Key.Left or Key.Right && entry.Property.IsExpandable)
        {
            var expanded = _expandedProperties.Contains(entry.Property.Name);
            if (expanded != (args.Key == Key.Right)) ToggleVisualStudioExpansion(entry.Property);
            args.Handled = true;
        }
    }
}
