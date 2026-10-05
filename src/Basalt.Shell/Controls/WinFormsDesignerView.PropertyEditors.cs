using System.ComponentModel;
using System.Drawing;
using Avalonia.Controls;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private async void OpenPropertyEditor(object component, DesignableProperty property)
    {
        if (_busy || _session is not { } session || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        Font? selectedFont = null;
        var selectedName = _selectedName;
        var propertyOffset = _properties.CapturePropertyEditorOffset();

        try
        {
            var original = session.Document;
            var descriptor = TypeDescriptor.GetProperties(component)[property.Name];
            if (descriptor is null || descriptor.IsReadOnly)
            {
                return;
            }

            object? selectedValue;
            var currentValue = descriptor.GetValue(component);

            if (currentValue is Color color)
            {
                var editor = new WinFormsColorEditor(color);
                if (!await editor.ShowDialog<bool>(owner))
                {
                    return;
                }

                selectedValue = editor.SelectedColor;
            }
            else if (currentValue is Font font)
            {
                var editor = new WinFormsFontEditor(font);
                if (!await editor.ShowDialog<bool>(owner))
                {
                    return;
                }

                selectedFont = editor.SelectedFont;
                selectedValue = selectedFont;
            }
            else if (property.IsComponentReference)
            {
                var selection = await SelectComponentReferenceAsync(session, property.ValueType, currentValue, owner);
                if (!selection.Accepted)
                {
                    return;
                }

                selectedValue = selection.Value;
            }
            else
            {
                return;
            }

            if (!ReferenceEquals(_session, session) || !ReferenceEquals(session.Document, original))
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            }

            // Serialization occurs before the returned edit task waits for parsing.
            // The modal's temporary font can therefore be released in finally.
            RunEditAsync(() => session.SetPropertyValueAsync(component, property.Name, selectedValue));
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            selectedFont?.Dispose();
            if (_attached && ReferenceEquals(_session, session) && _selectedName == selectedName)
            {
                _properties.RestoreSelectedPropertyFocus(propertyOffset);
            }
        }
    }

    private static async Task<(bool Accepted, object? Value)> SelectComponentReferenceAsync(
        WinFormsDesignerSession session,
        Type expectedType,
        object? currentValue,
        Window owner)
    {
        var choices = new List<ComponentReferenceChoice>
        {
            new(Localizer.Get(StringKeys.PropertyGridNone), null),
        };

        foreach (var control in session.Document.Controls)
        {
            var preview = session.FindControl(control.Name);
            if (preview is not null && expectedType.IsInstanceOfType(preview))
            {
                choices.Add(new ComponentReferenceChoice(control.Name, preview));
            }
        }

        foreach (var (name, component) in session.Components)
        {
            if (expectedType.IsInstanceOfType(component))
            {
                choices.Add(new ComponentReferenceChoice(name, component));
            }
        }

        var selector = new ComboBox
        {
            Name = "WinFormsComponentReference",
            ItemsSource = choices,
            SelectedItem = choices.FirstOrDefault(choice => ReferenceEquals(choice.Value, currentValue)) ?? choices[0],
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        var accept = new Button
        {
            Content = Localizer.Get(StringKeys.WinFormsEditorOk),
            IsDefault = true,
        };
        var cancel = new Button
        {
            Content = Localizer.Get(StringKeys.WinFormsEditorCancel),
            IsCancel = true,
        };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { accept, cancel },
        };
        var layout = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children = { selector, buttons },
        };
        var dialog = new Window
        {
            Title = Localizer.Get(StringKeys.PropertyGridComponentReference),
            Width = 420,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = layout,
        };

        accept.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        var accepted = await dialog.ShowDialog<bool>(owner);
        return (accepted, (selector.SelectedItem as ComponentReferenceChoice)?.Value);
    }

    private sealed record ComponentReferenceChoice(string Name, object? Value)
    {
        public override string ToString() => Name;
    }
}
