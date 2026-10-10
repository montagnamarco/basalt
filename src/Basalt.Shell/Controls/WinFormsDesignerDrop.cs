using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FormsControl = System.Windows.Forms.Control;
using DrawingPoint = System.Drawing.Point;

namespace Basalt.Shell.Controls;

/// <param name="Parent">
/// Where a visual control lands. A non-visual component (<see cref="Forms.Timer"/>,
/// <see cref="Forms.OpenFileDialog"/>, ...) has nowhere on the form to land - it always
/// goes to the component tray, the same place double-clicking it in the toolbox sends
/// it - so this is the root form for one of those, a sentinel rather than a real target.
/// </param>
public sealed record WinFormsControlDrop(Type ControlType, FormsControl Parent, DrawingPoint Location);

/// <summary>Resolves a drop in the receiving Forms container's client coordinates.</summary>
internal static class WinFormsDesignerDrop
{
    /// <remarks>
    /// The type is accepted by the same rule as a double-click in the toolbox: a Forms type, or a type of
    /// the project's own design context (see <see cref="WinFormsDesignTypes.IsDesignable"/>).
    /// </remarks>
    internal static WinFormsControlDrop? Resolve(Type type, Point at,
        Func<Point, FormsControl?> hitTest, Func<FormsControl, Rect> boundsOnHost, FormsControl root,
        WinFormsDesignAssemblyContext? context = null)
    {
        if (!typeof(Component).IsAssignableFrom(type) || !WinFormsDesignTypes.IsDesignable(type, context)
            || type.IsAbstract || type == typeof(System.Windows.Forms.Form)
            || type == typeof(System.Windows.Forms.ContextMenuStrip)) return null;

        // A toolbox item that is not itself a Control - Timer, OpenFileDialog, and the
        // rest of the "Components" and "Dialogs" toolbox groups - drops straight into
        // the tray wherever the pointer happens to be over the form: there is no visual
        // bounds to compute, and VB6/Visual Studio accept the drop anywhere on the form
        // for exactly this reason.
        if (!typeof(FormsControl).IsAssignableFrom(type))
            return new(type, root, DrawingPoint.Empty);

        var parent = hitTest(at);
        while (parent is not null && !CanContain(parent)) parent = parent.Parent;
        if (parent is null) return null;

        var bounds = boundsOnHost(parent);
        var insetX = parent.NonClient.Left;
        var insetY = parent.NonClient.Top;
        var x = Math.Max(0, (int)Math.Round(at.X - bounds.X - insetX));
        var y = Math.Max(0, (int)Math.Round(at.Y - bounds.Y - insetY));
        return new(type, parent, new DrawingPoint(x, y));
    }

    internal static void DrawPreview(Canvas adorners, WinFormsControlDrop? drop,
        Func<FormsControl, Rect> boundsOnHost)
    {
        adorners.Children.Clear();
        // A non-visual component has no bounds to preview - see Resolve above.
        if (drop is null || !typeof(FormsControl).IsAssignableFrom(drop.ControlType)) return;

        var parent = drop.Parent;
        var bounds = boundsOnHost(parent);
        var insetX = parent.NonClient.Left;
        var insetY = parent.NonClient.Top;
        var target = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(24, 30, 144, 255)),
        };
        Canvas.SetLeft(target, bounds.X);
        Canvas.SetTop(target, bounds.Y);
        adorners.Children.Add(target);

        var ghost = new Border
        {
            Width = 100,
            Height = 30,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(90, 30, 144, 255)),
        };
        Canvas.SetLeft(ghost, bounds.X + insetX + drop.Location.X);
        Canvas.SetTop(ghost, bounds.Y + insetY + drop.Location.Y);
        adorners.Children.Add(ghost);
    }

    internal static bool CanContain(FormsControl control) =>
        control is System.Windows.Forms.Form or System.Windows.Forms.Panel or
            System.Windows.Forms.GroupBox or System.Windows.Forms.TabPage or
            System.Windows.Forms.UserControl;
}
