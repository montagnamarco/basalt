using Avalonia.Controls;
using Basalt.Designer;
using Basalt.Core.Localization;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>Component properties with edits delegated to the document's undoable session.</summary>
public sealed partial class WinFormsPropertyPanel : UserControl
{
    private readonly PropertyGrid _grid = new()
    {
        VisualStudioStyle = true,
    };
    private Action<string, string?>? _edit;
    private Action<string>? _collectionRequested;
    private object? _component;
    private IReadOnlyList<DesignableProperty> _rootProperties = [];
    private readonly HashSet<string> _expandedParents = new(StringComparer.OrdinalIgnoreCase);
    private string? _componentName;
    private Action<DesignableProperty, string?>? _nestedEdit;
    private Action<DesignableProperty>? _openEditor;
    private string? _displayedObjectIdentity;

    public WinFormsPropertyPanel()
    {
        var layout = new DockPanel
        {
            Children = { _objects, _grid },
        };
        DockPanel.SetDock(_objects, Avalonia.Controls.Dock.Top);
        Content = layout;

        _grid.PropertyEdited += (name, value) =>
        {
            MarkEditFocus(name, value);
            _edit?.Invoke(name, value);
        };
        _grid.PropertyExpansionRequested += OnPropertyExpansionRequested;
        _grid.NestedPropertyEdited += (property, value) =>
        {
            MarkEditFocus(property.Name, value);
            _nestedEdit?.Invoke(property, value);
        };
        _grid.CanRequestPropertyEditor = CanOpenPropertyEditor;
        _grid.PropertyEditorRequested += OnPropertyEditorRequested;

        InitializeObjectSelector();
    }

    private void OnPropertyExpansionRequested(DesignableProperty property, bool expanded)
    {
        if (expanded)
        {
            _expandedParents.Add(property.Name);
        }
        else
        {
            _expandedParents.Remove(property.Name);
        }

        RebuildProperties();
    }

    private bool CanOpenPropertyEditor(DesignableProperty property)
    {
        if (property.IsReadOnly)
        {
            return false;
        }

        if (property.IsCollection)
        {
            return _collectionRequested is not null && property.Name is "Items" or "Columns";
        }

        return _openEditor is not null && property.HasCustomEditor;
    }

    private void OnPropertyEditorRequested(DesignableProperty property)
    {
        if (property.IsCollection)
        {
            _collectionRequested?.Invoke(property.Name);
        }
        else
        {
            _openEditor?.Invoke(property);
        }
    }

    /// <param name="explicitValue">Assignments in the document; null means an inherited default.</param>
    /// <param name="edit">Receives text assignments, or null to reset, for persistence and undo.</param>
    public void Show(object? control, Func<string, string?>? explicitValue = null,
        Action<string, string?>? edit = null, Action<string>? collectionRequested = null, string? componentName = null,
        Action<DesignableProperty, string?>? nestedEdit = null, Action<DesignableProperty>? editorRequested = null,
        Func<Type, IReadOnlyList<string>>? referenceNames = null,
        Func<string, WinFormsSourceValueState>? sourceState = null,
        Func<string, bool>? canResetResource = null,
        IReadOnlySet<string>? readOnlyProperties = null)
    {
        var identity = componentName ?? (control as Forms.Control)?.Name;
        var preserveView = control is not null && identity is not null &&
            (identity == _displayedObjectIdentity ||
                (_focusAfterEdit && identity == _expectedEditedIdentity));
        _displayedObjectIdentity = identity;
        _edit = edit;
        _nestedEdit = nestedEdit;
        _openEditor = editorRequested;
        _component = control;
        if (_componentName != componentName)
        {
            _expandedParents.Clear();
        }

        _componentName = componentName;
        _collectionRequested = collectionRequested;
        var properties = control is null ? [] : WinFormsPropertyInspector.DescribeDesigner(control, explicitValue)
            .Select(property =>
            {
                var resetState = ResetState(control, property, sourceState, canResetResource);
                return property with
                {
                    IsReadOnly = property.IsReadOnly || readOnlyProperties?.Contains(property.Name) == true,
                    // Runtime defaults also include constructor defaults that have no attribute.
                    DefaultValue = DefaultDisplayValue(control, property),
                    IsModifiedOverride = resetState?.IsModified,
                    CanResetOverride = readOnlyProperties?.Contains(property.Name) == true ? false : resetState?.CanReset,
                    EditorKind = property.IsComponentReference ? PropertyEditorKind.Enumeration : property.EditorKind,
                    AllowedValues = property.IsComponentReference && referenceNames is not null
                        ? new[] { Localizer.Get(StringKeys.PropertyGridNone) }.Concat(referenceNames(property.ValueType)).ToArray()
                        : property.AllowedValues,
                    ImageThumbnail = ImageThumbnailFor(control!, property),
                };
            }).ToArray();
        _rootProperties = componentName is null ? properties :
            [new DesignableProperty("Name", typeof(string), PropertyEditorKind.Text, PropertyCategory.Common, [], null)
                {
                    DisplayName = Localizer.Get(StringKeys.PropertyGridName),
                    DefaultValue = componentName,
                    IsReadOnly = readOnlyProperties?.Contains("Name") == true,
                    CanResetOverride = false,
                }, .. properties.Where(property => property.Name != "Name")];
        RebuildProperties(preserveView);
    }

    private static WinFormsPropertyResetState? ResetState(object component, DesignableProperty property,
        Func<string, WinFormsSourceValueState>? sourceState, Func<string, bool>? canResetResource)
    {
        var descriptor = System.ComponentModel.TypeDescriptor.GetProperties(component)[property.Name];
        return sourceState is null || descriptor is null ? null
            : WinFormsPropertyResetMetadata.Describe(component, descriptor, sourceState(property.Name),
                canResetResource?.Invoke(property.Name) == true);
    }

    private static string? DefaultDisplayValue(object component, DesignableProperty property)
    {
        if (property.IsCollection)
        {
            return Localizer.Get(StringKeys.PropertyGridCollection);
        }

        if (property.IsComponentReference)
        {
            return Localizer.Get(StringKeys.PropertyGridNone);
        }

        if (component is Forms.Control control && property.ValueType == typeof(System.Drawing.Color))
        {
            var declared = DeclaredAmbientColorValue(control, property.Name);
            if (declared is not null)
            {
                return declared;
            }
        }

        return WinFormsPropertyInspector.ReadDesignerValue(component, property.Name);
    }

    /// <summary>
    /// BackColor and ForeColor read live through a getter that, when unset, blends in
    /// the portable visual styles palette so the design surface paints faithfully
    /// without a real system theme (<c>Control.PortableVisualStylesEnabled</c>); that
    /// plain RGB is not what Visual Studio's Properties grid names for an unset
    /// ambient colour ("Control", "Window"...). When the property has not been
    /// assigned, show the control's declared WinForms default instead, named by the
    /// same converter a set value would use. Returns null for a set property, or one
    /// with no declared default, so the caller falls back to the live value.
    /// </summary>
    private static string? DeclaredAmbientColorValue(Forms.Control control, string propertyName)
    {
        var descriptor = System.ComponentModel.TypeDescriptor.GetProperties(control)[propertyName];
        if (descriptor is null || descriptor.ShouldSerializeValue(control))
        {
            return null;
        }

        var declared = control.GetDeclaredDefaultColor(propertyName);
        return declared.IsEmpty ? null
            : System.ComponentModel.TypeDescriptor.GetConverter(typeof(System.Drawing.Color)).ConvertToInvariantString(declared);
    }

    /// <summary>Shows only properties shared by a selected control group.</summary>
    public void ShowProperties(IReadOnlyList<DesignableProperty> properties, Action<string, string?> edit)
    {
        var preserveView = _component is null && _rootProperties.Count > 0;
        _displayedObjectIdentity = null;
        _edit = edit;
        _collectionRequested = null;
        _component = null;
        _nestedEdit = null;
        _openEditor = null;
        _expandedParents.Clear();
        _rootProperties = properties;
        SetGridProperties(properties, preserveView);
    }

    private void RebuildProperties(bool preserveView = true)
    {
        var rows = new List<DesignableProperty>();
        foreach (var property in _rootProperties)
        {
            rows.Add(property);
            if (_component is not null && property.IsExpandable && _expandedParents.Contains(property.Name))
            {
                rows.AddRange(WinFormsExpandableProperties.Children(_component, property.Name, property.IsSet));
            }
        }

        SetGridProperties(rows, preserveView);
    }
}
