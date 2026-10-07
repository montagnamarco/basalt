using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FormsControl = System.Windows.Forms.Control;
using DrawingSize = System.Drawing.Size;

namespace Basalt.Shell.Controls;

/// <summary>Paints a form's design grid in logical client coordinates.</summary>
internal sealed class WinFormsDesignerGrid : Control
{
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.FromRgb(148, 148, 148));
    private const double Gutter = 12;

    private FormsControl? _rootForm;
    private bool _showGrid = true;
    private DrawingSize _gridSize = new(8, 8);

    internal FormsControl? RootForm
    {
        get => _rootForm;
        set
        {
            _rootForm = value;
            InvalidateVisual();
        }
    }

    internal bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            InvalidateVisual();
        }
    }

    internal DrawingSize GridSize
    {
        get => _gridSize;
        set
        {
            _gridSize = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!_showGrid || _rootForm is null) return;

        var rootClient = new Rect(Gutter + _rootForm.NonClient.Left, Gutter + _rootForm.NonClient.Top,
            _rootForm.ClientSize.Width, _rootForm.ClientSize.Height);
        PaintContainer(context, _rootForm, rootClient.TopLeft, rootClient, []);
    }

    private void PaintContainer(DrawingContext context, FormsControl container,
        Point origin, Rect visible, IReadOnlyList<Rect> frontControls)
    {
        using var clip = context.PushClip(visible);
        var client = container.ClientSize;
        for (var y = _gridSize.Height; y < client.Height; y += _gridSize.Height)
        {
            for (var x = _gridSize.Width; x < client.Width; x += _gridSize.Width)
            {
                var point = new Point(origin.X + x, origin.Y + y);
                if (CoveredByChild(container, x, y) || CoveredByFrontControl(point, frontControls))
                    continue;
                context.DrawEllipse(DotBrush, null, point, 0.75, 0.75);
            }
        }

        var children = container.Controls.Cast<FormsControl>()
            .Where(child => child.DesiredVisibility).ToArray();
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            if (!WinFormsDesignerDrop.CanContain(child)) continue;
            var childOrigin = new Point(origin.X + child.Left + child.NonClient.Left,
                origin.Y + child.Top + child.NonClient.Top);
            var childClient = new Rect(childOrigin, new Size(child.ClientSize.Width, child.ClientSize.Height));
            var clipped = Intersect(visible, childClient);
            if (clipped.Width > 0 && clipped.Height > 0)
            {
                var occluders = new List<Rect>(frontControls);
                for (var front = 0; front < index; front++)
                {
                    var sibling = children[front];
                    occluders.Add(new Rect(origin.X + sibling.Left, origin.Y + sibling.Top,
                        sibling.Width, sibling.Height));
                }

                PaintContainer(context, child, childOrigin, clipped, occluders);
            }
        }
    }

    private static bool CoveredByChild(FormsControl container, int x, int y)
    {
        var dot = new Rect(x - 0.75, y - 0.75, 1.5, 1.5);
        foreach (FormsControl child in container.Controls)
        {
            if (!child.DesiredVisibility) continue;
            var bounds = new Rect(child.Left, child.Top, child.Width, child.Height);
            if (Intersect(bounds, dot) is { Width: > 0, Height: > 0 }) return true;
        }

        return false;
    }

    private static bool CoveredByFrontControl(Point point, IReadOnlyList<Rect> controls)
    {
        // The dot has a radius; its centre alone is not enough at a control edge.
        var dot = new Rect(point.X - 0.75, point.Y - 0.75, 1.5, 1.5);
        return controls.Any(control => Intersect(control, dot) is { Width: > 0, Height: > 0 });
    }

    private static Rect Intersect(Rect first, Rect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
