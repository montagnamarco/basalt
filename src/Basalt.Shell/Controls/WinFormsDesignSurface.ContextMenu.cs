using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsDesignSurface
{
    public event Action<bool>? ZOrderRequested;
    public event Action? PropertiesRequested;
    public event Action? CodeRequested;
    public event Action? DefaultEventRequested;
    public bool CanViewCode { get; set; } = true;

    private void InitializeContextMenu()
    {
        InitializeClipboardKeyboard();
        var copy = ContextItem("Designer_Copy", () => CopyRequested?.Invoke());
        var cut = ContextItem("Designer_Cut", () => CutRequested?.Invoke());
        var paste = ContextItem("Designer_Paste", () => PasteRequested?.Invoke());
        var delete = ContextItem("WinFormsDesigner_DeleteSelection", () =>
        {
            if (SelectedControl is { } selected && !ReferenceEquals(selected, _rootForm))
                DeleteRequested?.Invoke(selected);
        });
        var bringToFront = ContextItem("Designer_BringToFront", () => ZOrderRequested?.Invoke(true));
        var sendToBack = ContextItem("Designer_SendToBack", () => ZOrderRequested?.Invoke(false));
        var code = ContextItem("WinFormsDesigner_ViewCode", () => CodeRequested?.Invoke());
        var properties = ContextItem("WinFormsDesigner_Properties", () => PropertiesRequested?.Invoke());
        var selectAll = ContextItem("WinFormsDesigner_SelectSiblings", () =>
        {
            var parent = SelectedControl?.Parent ?? _rootForm;
            if (parent is not null)
                SelectControls(parent.Controls.Cast<System.Windows.Forms.Control>());
        });
        var align = new MenuItem { Header = Localizer.Get(StringKeys.WinFormsDesignerAlignSelection) };
        align.ItemsSource = new[]
        {
            LayoutContextItem(WinFormsLayoutCommand.AlignLeft, "WinFormsDesigner_AlignLeft"),
            LayoutContextItem(WinFormsLayoutCommand.AlignCenter, "WinFormsDesigner_AlignCenter"),
            LayoutContextItem(WinFormsLayoutCommand.AlignRight, "WinFormsDesigner_AlignRight"),
            LayoutContextItem(WinFormsLayoutCommand.AlignTop, "WinFormsDesigner_AlignTop"),
            LayoutContextItem(WinFormsLayoutCommand.AlignMiddle, "WinFormsDesigner_AlignMiddle"),
            LayoutContextItem(WinFormsLayoutCommand.AlignBottom, "WinFormsDesigner_AlignBottom"),
        };
        var distribute = new MenuItem { Header = Localizer.Get(StringKeys.WinFormsDesignerDistributeSelection) };
        distribute.ItemsSource = new[]
        {
            LayoutContextItem(WinFormsLayoutCommand.DistributeHorizontal, "WinFormsDesigner_DistributeHorizontal"),
            LayoutContextItem(WinFormsLayoutCommand.DistributeVertical, "WinFormsDesigner_DistributeVertical"),
        };
        var tabOrder = ToggleContextItem("WinFormsDesigner_TabOrder", () => TabOrderMode, value => TabOrderMode = value);
        var showGrid = ToggleContextItem("WinFormsDesigner_ShowGrid", () => ShowGrid, value => ShowGrid = value);
        var showRulers = ToggleContextItem("WinFormsDesigner_ShowRulers", () => ShowRulers, value => ShowRulers = value);
        var snapGrid = ToggleContextItem("WinFormsDesigner_SnapToGrid", () => SnapToGrid, value => SnapToGrid = value);
        var snapControls = ToggleContextItem("WinFormsDesigner_SnapToControls", () => SnapToControls, value => SnapToControls = value);
        var menu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                code, properties, new Separator(), cut, copy, paste, delete, selectAll, new Separator(), align, distribute,
                new Separator(), bringToFront, sendToBack, new Separator(), tabOrder, showGrid, showRulers, snapGrid, snapControls,
            },
        };
        void RefreshAvailability()
        {
            var count = _selection.Count(control => !ReferenceEquals(control, _rootForm));
            var sameParent = count > 0 && count == _selection.Count
                && _selection.All(control => ReferenceEquals(control.Parent, _selection[0].Parent));
            bringToFront.IsEnabled = sameParent && !TabOrderMode;
            sendToBack.IsEnabled = sameParent && !TabOrderMode;
            copy.IsEnabled = count > 0 && !TabOrderMode;
            cut.IsEnabled = count > 0 && !TabOrderMode;
            paste.IsEnabled = _rootForm is not null && !TabOrderMode && CanPasteControls?.Invoke() == true;
            code.IsEnabled = CanViewCode && SelectedControl is not null;
            properties.IsEnabled = SelectedControl is not null;
            delete.IsEnabled = count > 0 && !TabOrderMode;
            selectAll.IsEnabled = _rootForm is not null && !TabOrderMode;
            align.IsEnabled = count > 1 && !TabOrderMode;
            distribute.IsEnabled = count > 2 && !TabOrderMode;
            tabOrder.IsChecked = TabOrderMode;
            showGrid.IsChecked = ShowGrid;
            showRulers.IsChecked = ShowRulers;
            snapGrid.IsChecked = SnapToGrid;
            snapControls.IsChecked = SnapToControls;
        }
        menu.Opening += (_, _) => RefreshAvailability();
        menu.Opened += (_, _) => RefreshAvailability();
        ContextMenu = menu;
        DoubleTapped += (_, args) =>
        {
            if (args.Handled || IsInlineMenuInput(args.Source)) return;
            if (_rootForm is null || TabOrderMode) return;
            var control = HitTest(args.GetPosition(_host));
            if (control is null) return;
            Select(control);
            DefaultEventRequested?.Invoke();
            args.Handled = true;
        };
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!args.Handled && args.Key == Key.F4 && SelectedControl is not null)
            {
                PropertiesRequested?.Invoke();
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, (_, args) =>
        {
            if (_rootForm is null || !args.TryGetPosition(_host, out var point))
                return;
            var control = HitTest(point);
            // Right-clicking a member of the current group keeps that group.
            // A different control becomes the target before commands open.
            if (control is not null && !_selection.Contains(control))
                Select(control);
        }, RoutingStrategies.Tunnel);
    }

    private MenuItem LayoutContextItem(WinFormsLayoutCommand command, string key) =>
        ContextItem(key, () => ApplyLayoutCommand(command));

    private static MenuItem ContextItem(string key, Action action)
    {
        var item = new MenuItem { Header = Localizer.Get(key) };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem ToggleContextItem(string key, Func<bool> current, Action<bool> set)
    {
        var item = ContextItem(key, () => set(!current()));
        item.ToggleType = MenuItemToggleType.CheckBox;
        return item;
    }
}
