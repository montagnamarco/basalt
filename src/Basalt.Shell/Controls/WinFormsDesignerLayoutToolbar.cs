using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

/// <summary>Design-time layout actions; the view commits commands through its session.</summary>
internal sealed class WinFormsDesignerLayoutToolbar : StackPanel
{
    private readonly ToggleButton _tabOrder;
    private readonly Dictionary<WinFormsLayoutCommand, Button> _buttons = [];
    private int _selectedCount;

    internal WinFormsDesignerLayoutToolbar()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 4;

        _tabOrder = new ToggleButton { Content = Localizer.Get(StringKeys.WinFormsDesignerTabOrder) };
        _tabOrder.Click += (_, _) => OnTabOrderModeChanged(_tabOrder.IsChecked == true);
        Children.Add(_tabOrder);

        Add(WinFormsLayoutCommand.AlignLeft, "WinFormsDesigner_AlignLeft");
        Add(WinFormsLayoutCommand.AlignCenter, "WinFormsDesigner_AlignCenter");
        Add(WinFormsLayoutCommand.AlignRight, "WinFormsDesigner_AlignRight");
        Add(WinFormsLayoutCommand.AlignTop, "WinFormsDesigner_AlignTop");
        Add(WinFormsLayoutCommand.AlignMiddle, "WinFormsDesigner_AlignMiddle");
        Add(WinFormsLayoutCommand.AlignBottom, "WinFormsDesigner_AlignBottom");
        Add(WinFormsLayoutCommand.DistributeHorizontal, "WinFormsDesigner_DistributeHorizontal");
        Add(WinFormsLayoutCommand.DistributeVertical, "WinFormsDesigner_DistributeVertical");
        UpdateEnabled();
    }

    internal event Action<WinFormsLayoutCommand>? CommandRequested;
    internal event Action<bool>? TabOrderModeChanged;

    internal int SelectedCount
    {
        get => _selectedCount;
        set
        {
            _selectedCount = value;
            UpdateEnabled();
        }
    }

    internal bool TabOrderMode
    {
        get => _tabOrder.IsChecked == true;
        set
        {
            _tabOrder.IsChecked = value;
            UpdateEnabled();
        }
    }

    private void Add(WinFormsLayoutCommand command, string key)
    {
        var caption = Localizer.Get(key);
        var button = new Button
        {
            Content = WinFormsDesignerLayoutIcons.Create(command),
            Width = 32,
            Height = 28,
            Padding = new Thickness(4),
        };
        ToolTip.SetTip(button, caption);
        AutomationProperties.SetName(button, caption);
        button.Click += (_, _) => CommandRequested?.Invoke(command);
        _buttons[command] = button;
        Children.Add(button);
    }

    private void UpdateEnabled()
    {
        foreach (var (command, button) in _buttons)
            button.IsEnabled = !TabOrderMode && _selectedCount >=
                (command is WinFormsLayoutCommand.DistributeHorizontal or WinFormsLayoutCommand.DistributeVertical ? 3 : 2);
    }

    private void OnTabOrderModeChanged(bool enabled)
    {
        UpdateEnabled();
        TabOrderModeChanged?.Invoke(enabled);
    }
}
