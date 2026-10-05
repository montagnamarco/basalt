namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private void UpdateObjectSelector()
    {
        if (_session is not { } session) return;
        var objects = new[] { new WinFormsDesignerObject("Me", session.Document.ClassName + " System.Windows.Forms.Form") }
            .Concat(session.Document.Controls.Select(control => new WinFormsDesignerObject(control.Name,
                control.Name + " " + control.TypeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase))))
            .Concat(session.Document.Components.Select(component => new WinFormsDesignerObject(component.Name,
                component.Name + " " + component.TypeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        _properties.ShowObjects(objects, _surface.SelectedControls.Count > 1 ? null : _selectedName);
    }
}
