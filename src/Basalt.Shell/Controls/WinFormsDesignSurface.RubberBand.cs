using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FormsControl = System.Windows.Forms.Control;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsDesignSurface
{
    /// <summary>Where a rubber-band selection started, in host-canvas coordinates, while one is running.</summary>
    private Point? _bandOrigin;
    private Point _bandTo;
    private bool _bandAdds;
    private Border? _bandAdorner;

    /// <summary>
    /// Starts a rubber-band drag from a press on the form's own background.
    /// </summary>
    /// <remarks>
    /// A press that hits a control, rather than the form itself, is already a move or
    /// resize gesture (<see cref="OnPointerPressed"/>) - the band is the other case, the one
    /// VB6 and Visual Studio answer by drawing a selection rectangle instead of moving
    /// anything, because there is nothing under the pointer to move.
    /// </remarks>
    private void StartBand(Point at, bool additive = false)
    {
        _bandOrigin = at;
        _bandAdds = additive;
        _bandTo = at;
        _bandAdorner = new Border
        {
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(30, 30, 144, 255)),
            IsHitTestVisible = false,
        };
        _adorners.Children.Add(_bandAdorner);
        DrawBand();
    }

    private void UpdateBand(Point at)
    {
        if (_bandOrigin is null) return;
        _bandTo = at;
        DrawBand();
    }

    /// <summary>Finishes the band, selecting whatever it wholly covers.</summary>
    private void EndBand()
    {
        if (_bandOrigin is not { } origin) return;

        var band = Band(origin, _bandTo);
        _bandOrigin = null;
        RemoveBandAdorner();
        SelectWithinBand(band);
    }

    /// <summary>Abandons the band without changing the selection: Escape, or losing capture.</summary>
    private void CancelBand()
    {
        _bandOrigin = null;
        RemoveBandAdorner();
    }

    private void RemoveBandAdorner()
    {
        if (_bandAdorner is null) return;
        _adorners.Children.Remove(_bandAdorner);
        _bandAdorner = null;
    }

    private void DrawBand()
    {
        if (_bandOrigin is not { } origin || _bandAdorner is null) return;

        var band = Band(origin, _bandTo);
        _bandAdorner.Width = band.Width;
        _bandAdorner.Height = band.Height;
        Canvas.SetLeft(_bandAdorner, band.X);
        Canvas.SetTop(_bandAdorner, band.Y);
    }

    private static Rect Band(Point from, Point to) => new(
        Math.Min(from.X, to.X), Math.Min(from.Y, to.Y),
        Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));

    /// <summary>
    /// Selects every control the band wholly covers.
    /// </summary>
    /// <remarks>
    /// Wholly covered, not merely touched: a band drawn across a busy form brushes half of
    /// it, and picking up everything it grazed is never what was meant - Visual Studio draws
    /// the same line. A band smaller than the drag threshold is the click that started it,
    /// already handled as a plain selection by <see cref="OnPointerPressed"/>, so it leaves
    /// the selection alone here rather than clearing it.
    /// </remarks>
    private void SelectWithinBand(Rect band)
    {
        if (_rootForm is null) return;
        if (band.Width < DragThreshold && band.Height < DragThreshold) return;

        // The form's own children only, as in Visual Studio: a band drawn on the form does not reach
        // into a Panel or GroupBox to pick a control out of it without its container.
        var caught = _rootForm.Controls.Cast<FormsControl>()
            .Where(control => control.DesiredVisibility && band.Contains(BoundsOnHost(control)))
            .ToArray();

        if (_bandAdds)
        {
            var kept = _selection.Where(control => !ReferenceEquals(control, _rootForm));
            caught = kept.Concat(caught).Distinct().ToArray();
        }

        if (caught.Length > 0) SelectControls(caught);
    }
}
