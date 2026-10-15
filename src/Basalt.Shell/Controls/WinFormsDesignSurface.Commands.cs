using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Basalt.Shell.Controls;

/// <summary>
/// The view state the Designer and Format menus change: zoom and locking. Neither is saved with the form: Visual Studio
/// keeps the zoom per window and the lock per designer session, and so does this surface.
/// </summary>
public sealed partial class WinFormsDesignSurface
{
    public const double MinimumZoom = 0.25;
    public const double MaximumZoom = 4;

    private double _zoom = 1;

    /// <summary>The scale the form is drawn at. The design surface scales its whole canvas, so pointer positions follow.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            var wanted = Math.Clamp(value, MinimumZoom, MaximumZoom);
            if (Math.Abs(wanted - _zoom) < 0.001) return;

            _zoom = wanted;
            // Scaled about the top left, as the XAML designer scales its surface: the form's origin stays where the
            // user sees it. The layout keeps the unscaled size, and MinWidth/MinHeight (see UpdateSize) reserve the rest.
            _host.RenderTransformOrigin = RelativePoint.TopLeft;
            _host.RenderTransform = new ScaleTransform(wanted, wanted);
            RefreshForm();
        }
    }

    /// <summary>
    /// Scales the form so that it fits the space the surface has, without zooming past 100 percent: a small form is
    /// never blown up to fill the window.
    /// </summary>
    public void ZoomToFit()
    {
        var space = VisibleSpace();
        if (space.Width <= 0 || space.Height <= 0 || _host.Width <= 0 || _host.Height <= 0) return;

        // The host's own size is unscaled: at zoom z the form occupies z times it.
        var fit = Math.Min(space.Width / _host.Width, space.Height / _host.Height);
        Zoom = Math.Min(1, fit);
    }

    /// <summary>
    /// The space the user sees the surface in: the viewport of the scroll viewer that holds it. Not the surface's own bounds,
    /// which are at least the form drawn at the current zoom (see UpdateSize), so a large form could never shrink.
    /// </summary>
    private Size VisibleSpace() =>
        this.FindAncestorOfType<ScrollViewer>() is { Viewport: { Width: > 0, Height: > 0 } viewport } ? viewport : Bounds.Size;

    /// <summary>
    /// When true, controls can be selected and deleted but not moved, resized, aligned or nudged by any means, as Visual
    /// Studio's Lock Controls.
    /// </summary>
    public bool LockControls { get; set; }
}
