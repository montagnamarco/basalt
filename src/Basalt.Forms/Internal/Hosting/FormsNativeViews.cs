using Avalonia;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>Positions platform-rendered browser surfaces over the drawn form.</summary>
internal sealed class FormsNativeViews(FormsCanvas canvas)
{
    private readonly Dictionary<WebBrowser, Avalonia.Controls.Control> _views = [];
    private bool _synchronizing;

    internal void Synchronize()
    {
        if (_synchronizing) return;
        _synchronizing = true;
        try
        {
            var descendants = Enumerate(canvas.Root).OfType<WebBrowser>().Where(browser => !browser.IsDisposed).ToArray();
            foreach (var removed in _views.Keys.Except(descendants).ToArray())
            {
                canvas.DetachNativeView(_views[removed]);
                _views.Remove(removed);
            }

            foreach (var browser in descendants)
            {
                if (!browser.Visible)
                {
                    // Keep an existing native adapter in the visual tree so
                    // hide/show preserves the page and its navigation history.
                    if (_views.TryGetValue(browser, out var hiddenView)) hiddenView.IsVisible = false;
                    continue;
                }

                if (!_views.TryGetValue(browser, out var view))
                {
                    view = browser.NativeView;
                    _views.Add(browser, view);
                    canvas.AttachNativeView(view);
                }

                var bounds = BoundsInCanvas(browser);
                var clip = bounds;
                for (var ancestor = browser.Parent; ancestor is not null; ancestor = ancestor.Parent)
                    clip.Intersect(BoundsInCanvas(ancestor));

                view.IsEnabled = browser.Enabled;
                view.IsVisible = clip.Width > 0 && clip.Height > 0;
                view.Measure(new Size(bounds.Width, bounds.Height));
                view.Arrange(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                Geometry visibleGeometry = new RectangleGeometry(new Rect(clip.X - bounds.X, clip.Y - bounds.Y,
                    Math.Max(0, clip.Width), Math.Max(0, clip.Height)));

                // This geometry is useful for compositor-backed adapters and
                // records which parts of the browser should remain visible.
                // NativeControlHost on macOS/GTK positions a child window by
                // rectangle and ignores Avalonia's Clip, so it does not yet
                // solve native airspace occlusion there.
                for (Control branch = browser; branch.Parent is { } parent; branch = parent)
                {
                    foreach (var sibling in parent.Controls.Cast<Control>())
                    {
                        if (ReferenceEquals(sibling, branch)) break;
                        if (!sibling.Visible || sibling.IsDisposed) continue;

                        var covering = BoundsInCanvas(sibling);
                        covering.Intersect(bounds);
                        if (covering.Width <= 0 || covering.Height <= 0) continue;

                        var coveringGeometry = new RectangleGeometry(new Rect(
                            covering.X - bounds.X, covering.Y - bounds.Y,
                            covering.Width, covering.Height));
                        visibleGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, visibleGeometry, coveringGeometry);
                    }
                }

                view.Clip = visibleGeometry;
            }

            var firstNativeIndex = canvas.GetVisualChildren().Count() - _views.Count;
            var attached = descendants.Where(_views.ContainsKey).ToArray();
            for (var i = 0; i < attached.Length; i++)
                canvas.ReorderNativeView(_views[attached[i]], firstNativeIndex + i);
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private Drawing.Rectangle BoundsInCanvas(Control control)
    {
        var origin = canvas.Root.PointToClient(control.PointToScreen(Drawing.Point.Empty));
        return new Drawing.Rectangle(origin, control.ClientSize);
    }

    private static IEnumerable<Control> Enumerate(Control parent)
    {
        // WinForms index zero is the front; visual children paint back to front.
        foreach (var child in parent.Controls.Cast<Control>().Reverse())
        {
            yield return child;
            foreach (var descendant in Enumerate(child)) yield return descendant;
        }
    }
}
