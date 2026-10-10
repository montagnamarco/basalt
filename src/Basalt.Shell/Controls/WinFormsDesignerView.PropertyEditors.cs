using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.IO;
using Avalonia.Controls;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Workspace;
using Forms = System.Windows.Forms;
using FormsDesign = System.Windows.Forms.Design;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    /// <summary>The modals opened from the designer that are open now; see <see cref="BeginModal"/>.</summary>
    private int _openModals;

    /// <summary>
    /// Holds the view busy for design-context swaps while a modal opened from the designer is open. The modal's
    /// result is written to the session it started on, so a build that finishes meanwhile waits until the modal
    /// closes (see <see cref="ApplyPendingDesignContext"/>). Without this a build swapped the controls under the
    /// open editor, and the edit then failed or was lost.
    /// </summary>
    private IDisposable BeginModal()
    {
        _openModals++;
        return new ModalScope(this);
    }

    private void EndModal()
    {
        _openModals--;
        if (_openModals == 0) ApplyPendingDesignContext();
    }

    /// <summary>Opens a modal as an open property editor does, until the returned scope is disposed; for the designer's tests.</summary>
    internal IDisposable HoldModalForTests() => BeginModal();

    /// <summary>Ends a modal's hold on the view once, however many times it is disposed.</summary>
    private sealed class ModalScope(WinFormsDesignerView view) : IDisposable
    {
        private WinFormsDesignerView? _view = view;

        public void Dispose()
        {
            var owner = _view;
            _view = null;
            owner?.EndModal();
        }
    }

    private async void OpenPropertyEditor(object component, DesignableProperty property)
    {
        if (_busy || _session is not { } session || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        // Held until the editor has closed and its edit, if any, has started: the edit then holds the view itself.
        using var modal = BeginModal();
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

            if (IsImageProperty(descriptor.PropertyType) && component is Forms.Control imageControl)
            {
                if (RefuseImageEditBeforeDialog(session))
                {
                    return;
                }

                var isIcon = descriptor.PropertyType.FullName == "System.Drawing.Icon";
                var projectPath = WinFormsProjectResources.FindProject(_document.FilePath);
                var editor = new WinFormsImageResourceDialog(EncodeCurrentImage(currentValue), isIcon, _importImageFile, projectPath);
                if (!await editor.ShowDialog<bool>(owner))
                {
                    return;
                }

                if (!ReferenceEquals(_session, session) || !ReferenceEquals(session.Document, original))
                {
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
                }

                if (editor.UsedProjectResource && editor.SelectedProjectResourceName is { } resourceName)
                {
                    var rootNamespace = WinFormsProjectResources.RootNamespace(projectPath!);
                    RunEditAsync(() => session.SetProjectImageResourcePropertyAsync(imageControl, property.Name, rootNamespace, resourceName));
                }
                else if (editor.Cleared)
                {
                    RunEditAsync(() => session.ClearImageResourcePropertyAsync(imageControl, property.Name));
                }
                else if (editor.Changed && editor.SelectedBytes is { } bytes)
                {
                    RunEditAsync(() => session.SetImageResourcePropertyAsync(imageControl, property.Name, bytes, isIcon));
                }
                return;
            }

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
            else if (TryGetDesignTimeEditor(descriptor) is { } editor)
            {
                if (!TryInvokeDesignTimeEditor(editor, component, descriptor, currentValue, session.Form, out var edited))
                {
                    return;
                }

                selectedValue = edited;
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

    /// <summary>Image, BackgroundImage, InitialImage, ErrorImage and Icon: edited through "Select Resource" rather than text.</summary>
    internal static bool IsImageProperty(Type type) => type.FullName is
        "System.Drawing.Image" or "System.Drawing.Bitmap" or "System.Drawing.Icon";

    /// <summary>The bytes an already-assigned Image or Icon would round-trip as, for the dialog's starting preview.</summary>
    private static byte[]? EncodeCurrentImage(object? value)
    {
        switch (value)
        {
            case Icon icon:
                using (var stream = new MemoryStream())
                {
                    icon.Save(stream);
                    return stream.ToArray();
                }
            case System.Drawing.Image image:
                return (byte[]?)new ImageConverter().ConvertTo(image, typeof(byte[]));
            default:
                return null;
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

    /// <summary>
    /// The first <c>[Editor(..., typeof(UITypeEditor))]</c> whose edit style is not
    /// None: a Modal one shows a "..." dialog, a DropDown one a popup under the row.
    /// <see cref="Basalt.Designer.WinFormsPropertyInspector"/> decides whether the row
    /// gets a "..." button from the same attribute, by reflection (it has no reference
    /// to this assembly); here, with the reference, the real type can be asked directly.
    /// </summary>
    internal static UITypeEditor? TryGetDesignTimeEditor(PropertyDescriptor descriptor) =>
        descriptor.GetEditor(typeof(UITypeEditor)) is UITypeEditor editor
            && editor.GetEditStyle() != UITypeEditorEditStyle.None ? editor : null;

    /// <summary>
    /// Runs a UITypeEditor's EditValue with a working IWindowsFormsEditorService, the
    /// same contract a real Windows Forms designer offers: a Modal editor calls
    /// ShowDialog on its own dialog Form, a DropDown editor calls DropDownControl with
    /// the control to host under the row. Both block synchronously until the editor is
    /// done, the same way Basalt.Forms's own Form.ShowDialog already does.
    /// </summary>
    internal static bool TryInvokeDesignTimeEditor(UITypeEditor editor, object component, PropertyDescriptor descriptor,
        object? currentValue, Forms.Form owner, out object? value)
    {
        var context = new DesignTimeEditorContext(component, descriptor);
        using var service = new DesignTimeEditorService(owner);
        value = editor.EditValue(context, service, currentValue);
        return !Equals(value, currentValue);
    }

    /// <summary>
    /// The borderless host form for a drop-down editor's control, opened at <paramref name="location"/>
    /// (screen coordinates). Kept separate from <see cref="DesignTimeEditorService.DropDownControl"/>
    /// so the placement can be asserted without showing a window.
    /// </summary>
    internal static Forms.Form CreateDropDownForm(Forms.Control control, Point location)
    {
        var form = new Forms.Form
        {
            FormBorderStyle = Forms.FormBorderStyle.None,
            StartPosition = Forms.FormStartPosition.Manual,
            ShowInTaskbar = false,
            ClientSize = control.Size,
            Location = location,
        };
        form.Controls.Add(control);
        return form;
    }

    /// <summary>
    /// Top-left corner for a drop-down of <paramref name="size"/> opened at the screen
    /// <paramref name="pointer"/>, moved back inside <paramref name="workingArea"/> when it would
    /// run off the right or bottom edge (or the left or top edge for a pointer outside it). When
    /// the drop-down is larger than the area, it is pinned to the area's top-left corner.
    /// </summary>
    internal static Point DropDownLocation(Point pointer, Size size, Rectangle workingArea)
    {
        var maxX = Math.Max(workingArea.Left, workingArea.Right - size.Width);
        var maxY = Math.Max(workingArea.Top, workingArea.Bottom - size.Height);
        return new Point(
            Math.Clamp(pointer.X, workingArea.Left, maxX),
            Math.Clamp(pointer.Y, workingArea.Top, maxY));
    }

    private sealed class DesignTimeEditorContext(object instance, PropertyDescriptor descriptor) : ITypeDescriptorContext
    {
        public IContainer? Container => null;
        public object Instance => instance;
        public PropertyDescriptor PropertyDescriptor => descriptor;
        public object? GetService(Type serviceType) => null;
        public void OnComponentChanged() { }
        public bool OnComponentChanging() => true;
    }

    /// <summary>
    /// Hosts a UITypeEditor's dialog or drop-down control inside the Basalt.Forms Form
    /// being designed. Only editors whose types live in an assembly the host can load are
    /// reachable here at all - EditValue is called with the real control, never a stub -
    /// which today means editors shipped with Basalt.Forms (src/Basalt.Forms/Design).
    /// </summary>
    private sealed class DesignTimeEditorService(Forms.Form owner)
        : IServiceProvider, FormsDesign.IWindowsFormsEditorService, IDisposable
    {
        private Forms.Form? _dropDown;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(FormsDesign.IWindowsFormsEditorService) ? this : null;

        public Forms.DialogResult ShowDialog(Forms.Form dialog) => dialog.ShowDialog(owner);

        public void DropDownControl(Forms.Control control)
        {
            // The editor is opened synchronously by the click on the row's "..." button, so the
            // pointer's last recorded position is that click. Opening there, rather than at the
            // form's default (0,0), keeps the drop-down beside the row that asked for it.
            var pointer = Forms.Control.MousePosition;
            var workingArea = Forms.Screen.FromPoint(pointer).WorkingArea;
            _dropDown = CreateDropDownForm(control, DropDownLocation(pointer, control.Size, workingArea));
            _dropDown.ShowDialog(owner);
            _dropDown = null;
        }

        public void CloseDropDown() => _dropDown?.Close();

        public void Dispose() => _dropDown?.Dispose();
    }
}
