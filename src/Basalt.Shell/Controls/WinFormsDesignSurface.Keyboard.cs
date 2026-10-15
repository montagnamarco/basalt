using Avalonia.Input;
using FormsControl = System.Windows.Forms.Control;
using Rectangle = System.Drawing.Rectangle;

namespace Basalt.Shell.Controls;

/// <summary>
/// The keyboard as Visual Studio's form designer answers it. Arrows move the selection (by the grid when snapping is on,
/// by a pixel otherwise, and by a pixel with Ctrl whatever the grid). Shift with an arrow resizes it a pixel at a time.
/// Tab and Shift+Tab walk the tab order. Esc climbs from a control to its container and then to the form. Ctrl+A selects
/// the form's controls, and Backspace does nothing: only Delete removes a control.
/// </summary>
public sealed partial class WinFormsDesignSurface
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsInlineMenuInput(e.Source)) return;
        if (_tabOrderMode)
        {
            if (e.Key == Key.Escape)
            {
                TabOrderMode = false;
                e.Handled = true;
            }
            return;
        }
        if ((_gesture is not null || _bandOrigin is not null) && e.Key == Key.Escape)
        {
            CancelBand();
            CancelGesture();
            e.Handled = true;
            return;
        }
        if (_rootForm is null) return;

        var modifiers = e.KeyModifiers;
        switch (e.Key)
        {
            case Key.Tab when modifiers is KeyModifiers.None or KeyModifiers.Shift:
                CycleSelection(forward: modifiers == KeyModifiers.None);
                e.Handled = true;
                return;

            case Key.Escape when SelectedControl is not null && !ReferenceEquals(SelectedControl, _rootForm):
                SelectParent();
                e.Handled = true;
                return;

            case Key.A when modifiers is KeyModifiers.Control or KeyModifiers.Meta:
                SelectAllControls();
                e.Handled = true;
                return;

            case Key.Delete when SelectedControl is not null && !ReferenceEquals(SelectedControl, _rootForm):
                RequestDelete();
                e.Handled = true;
                return;

            case Key.Left or Key.Right or Key.Up or Key.Down:
                // Handled even when nothing moves: an unhandled arrow would hand focus to a neighbouring control.
                MoveOrResizeSelection(e);
                e.Handled = true;
                return;
        }
    }

    /// <summary>
    /// Selects the next control in tab order, or the previous one, wrapping at either end. The form is not in the cycle:
    /// from the form, a forward step selects the first control and a backward step the last.
    /// </summary>
    public void CycleSelection(bool forward)
    {
        if (_rootForm is null || _tabOrderMode) return;

        var order = TabOrderOf(_rootForm).ToList();
        if (order.Count == 0) return;

        var index = SelectedControl is { } current && !ReferenceEquals(current, _rootForm) ? order.IndexOf(current) : -1;
        var next = index < 0
            ? (forward ? 0 : order.Count - 1)
            : (index + (forward ? 1 : order.Count - 1)) % order.Count;
        Select(order[next]);
    }

    /// <summary>
    /// Selects the container of the selected control, skipping a container's own panel, which is not selectable. A control
    /// directly on the form selects the form, and the form has no parent to select.
    /// </summary>
    public void SelectParent()
    {
        if (SelectedControl is not { } selected || ReferenceEquals(selected, _rootForm)) return;

        var parent = selected.Parent;
        while (parent is not null && IsPanelOfContainer(parent)) parent = parent.Parent;
        if (parent is null || !BelongsToRoot(parent)) return;

        Select(parent);
    }

    /// <summary>
    /// Selects the controls of the form itself: its direct children that are visible at design time. The form's controls
    /// are one level deep in the selection rule (see SelectControls), so a control nested in a container is reached with
    /// Esc or a click, not by this.
    /// </summary>
    public void SelectAllControls()
    {
        if (_rootForm is null || _tabOrderMode) return;

        var controls = _rootForm.Controls.Cast<FormsControl>().Where(control => control.DesiredVisibility).ToArray();
        if (controls.Length == 0) return;

        SelectControls(controls, controls[^1]);
    }

    /// <summary>Asks the owner to delete the selected control, if it is one the form can delete (never the form itself).</summary>
    public void RequestDelete()
    {
        if (SelectedControl is { } selected && !ReferenceEquals(selected, _rootForm) && !_tabOrderMode)
            DeleteRequested?.Invoke(selected);
    }

    /// <summary>Asks the owner to bring the selection to the front, or send it to the back, as the context menu does.</summary>
    public void RequestZOrder(bool bringToFront) => ZOrderRequested?.Invoke(bringToFront);

    private void MoveOrResizeSelection(KeyEventArgs e)
    {
        if (SelectedControl is not { } selected || ReferenceEquals(selected, _rootForm) || LockControls) return;

        var (horizontal, vertical) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            _ => (0, 1),
        };

        var group = _selection.Select(control => (control, control.Bounds)).ToArray();
        var resize = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var nudge = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Shift+arrow resizes the control from its bottom-right corner, one pixel at a time: Shift+Right widens it and
        // Shift+Left narrows it, as Visual Studio does. Otherwise the
        // control moves one grid step when snapping is on and the key is bare; Ctrl+arrow is always a one-pixel nudge.
        var stepX = !nudge && SnapToGrid ? GridSize.Width : 1;
        var stepY = !nudge && SnapToGrid ? GridSize.Height : 1;
        foreach (var (control, before) in group)
        {
            control.Bounds = resize
                ? new Rectangle(before.X, before.Y,
                    Math.Max(1, before.Width + horizontal), Math.Max(1, before.Height + vertical))
                : new Rectangle(before.X + horizontal * stepX, before.Y + vertical * stepY,
                    before.Width, before.Height);
        }

        RefreshForm();

        if (group.Length > 1) CompleteGroup(group);
        else Complete(selected, group[0].Bounds);
    }

    /// <summary>
    /// The controls of the form in tab order, each one's own children straight after it, as the tab key walks them. The
    /// panels of a container have no tab order of their own and are skipped with their own position in the walk.
    /// </summary>
    private static IEnumerable<FormsControl> TabOrderOf(FormsControl parent)
    {
        foreach (FormsControl child in parent.Controls.Cast<FormsControl>().OrderBy(control => control.TabIndex))
        {
            if (!child.DesiredVisibility) continue;

            if (!IsPanelOfContainer(child)) yield return child;
            foreach (var nested in TabOrderOf(child)) yield return nested;
        }
    }
}
