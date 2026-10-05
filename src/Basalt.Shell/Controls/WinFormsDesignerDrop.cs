using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FormsControl = System.Windows.Forms.Control;
using DrawingPoint = System.Drawing.Point;

namespace Basalt.Shell.Controls;

public sealed record WinFormsControlDrop(Type ControlType, FormsControl Parent, DrawingPoint Location);

/// <summary>Resolves a drop in the receiving Forms container's client coordinates.</summary>
internal static class WinFormsDesignerDrop
{
    internal static WinFormsControlDrop? Resolve(Type type, FormsControl root, Point at,
        Func<Point, FormsControl?> hitTest, Func<FormsControl, Rect> boundsOnHost)
    {
        if (!typeof(FormsControl).IsAssignableFrom(type) || type.Assembly != typeof(FormsControl).Assembly
            || type.IsAbstract || type == typeof(System.Windows.Forms.Form)
            || type == typeof(System.Windows.Forms.ContextMenuStrip)) return null;

        var parent = hitTest(at);
        while (parent is not null && !CanContain(parent)) parent = parent.Parent;
        if (parent is null) return null;

        var bounds = boundsOnHost(parent);
        var insetX = IsRootContainer(parent, root) ? 0 : parent.NonClient.Left;
        var insetY = IsRootContainer(parent, root) ? 0 : parent.NonClient.Top;
        var x = Math.Max(0, (int)Math.Round(at.X - bounds.X - insetX));
        var y = Math.Max(0, (int)Math.Round(at.Y - bounds.Y - insetY));
        return new(type, parent, new DrawingPoint(x, y));
    }

    internal static void DrawPreview(Canvas adorners, WinFormsControlDrop? drop, FormsControl? root,
        Func<FormsControl, Rect> boundsOnHost)
    {
        adorners.Children.Clear();
        if (drop is null) return;

        var parent = drop.Parent;
        var bounds = boundsOnHost(parent);
        var insetX = IsRootContainer(parent, root) ? 0 : parent.NonClient.Left;
        var insetY = IsRootContainer(parent, root) ? 0 : parent.NonClient.Top;
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

    /// <summary>
    /// Whether <paramref name="candidate"/> is the design surface's root form, the one
    /// container that has no non-client chrome to offset against (it fills the whole
    /// host surface, so its client origin is the host origin).
    /// </summary>
    internal static bool IsRootContainer(FormsControl candidate, FormsControl? root) =>
        ReferenceEquals(candidate, root);

    internal static bool CanContain(FormsControl control) =>
        control is System.Windows.Forms.Form or System.Windows.Forms.Panel or
            System.Windows.Forms.GroupBox or System.Windows.Forms.TabPage or
            System.Windows.Forms.UserControl;
}
