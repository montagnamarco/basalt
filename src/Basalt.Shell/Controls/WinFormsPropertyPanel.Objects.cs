using Avalonia.Controls;
using Avalonia.Automation;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

public sealed record WinFormsDesignerObject(string Name, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed partial class WinFormsPropertyPanel
{
    private readonly ComboBox _objects = new()
    {
        Name = "WinFormsSelectedObject",
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
    };
    private bool _updatingObjects;
    public event Action<string>? ObjectSelectionRequested;

    public Control? EventsContent
    {
        get => _grid.EventsContent;
        set => _grid.EventsContent = value;
    }

    public Action? ExternalEventsRequested
    {
        get => _grid.ExternalEventsRequested;
        set => _grid.ExternalEventsRequested = value;
    }

    private void InitializeObjectSelector()
    {
        AutomationProperties.SetName(_objects, Localizer.Get(StringKeys.WinFormsDesignerObjects));
        _objects.SelectionChanged += (_, _) =>
        {
            if (!_updatingObjects && _objects.SelectedItem is WinFormsDesignerObject item && item.Name.Length > 0)
            {
                ObjectSelectionRequested?.Invoke(item.Name);
            }
        };
    }

    public void ShowObjects(IReadOnlyList<WinFormsDesignerObject> objects, string? selectedName)
    {
        _updatingObjects = true;

        try
        {
            var entries = selectedName is null
                ? new[] { new WinFormsDesignerObject("", Localizer.Get(StringKeys.WinFormsDesignerSelectedObjects)) }.Concat(objects).ToArray()
                : objects.ToArray();
            _objects.ItemsSource = entries;
            _objects.SelectedItem = selectedName is null ? entries[0]
                : entries.FirstOrDefault(item => item.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _updatingObjects = false;
        }
    }
}
