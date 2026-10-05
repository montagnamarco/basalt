using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

/// <summary>Selection and deletion surface for designer components without visual Bounds.</summary>
internal sealed class WinFormsComponentTray : UserControl
{
    private sealed record Entry(string Name, string TypeName)
    {
        public override string ToString() => $"{Name} ({TypeName[(TypeName.LastIndexOf('.') + 1)..]})";
    }

    private readonly ListBox _list = new() { Height = 84 };
    private bool _updating;

    public event Action<string?>? SelectionChanged;
    public event Action<string>? DeleteRequested;
    public event Action<string>? DefaultEventRequested;

    public string? SelectedName => (_list.SelectedItem as Entry)?.Name;

    public WinFormsComponentTray()
    {
        var title = new TextBlock { Text = Localizer.Get(StringKeys.WinFormsDesignerComponents) };
        var panel = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(title);
        DockPanel.SetDock(title, Avalonia.Controls.Dock.Top);
        panel.Children.Add(_list);
        Content = panel;

        _list.SelectionChanged += (_, _) =>
        {
            if (!_updating) SelectionChanged?.Invoke(SelectedName);
        };
        _list.KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Delete or Key.Back) || SelectedName is not { } name) return;
            DeleteRequested?.Invoke(name);
            args.Handled = true;
        };
        _list.DoubleTapped += (_, _) =>
        {
            if (SelectedName is { } name)
            {
                DefaultEventRequested?.Invoke(name);
            }
        };
    }

    public void Show(IReadOnlyList<WinFormsDesignerComponent> components)
    {
        var selected = SelectedName;
        _updating = true;
        try
        {
            _list.ItemsSource = components.Select(component => new Entry(component.Name, component.TypeName)).ToArray();
            SelectCore(selected);
        }
        finally { _updating = false; }
    }

    public void Select(string? name)
    {
        _updating = true;
        try { SelectCore(name); }
        finally { _updating = false; }
    }

    private void SelectCore(string? name) => _list.SelectedItem =
        (_list.ItemsSource ?? Array.Empty<object>()).OfType<Entry>().FirstOrDefault(entry =>
            entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
