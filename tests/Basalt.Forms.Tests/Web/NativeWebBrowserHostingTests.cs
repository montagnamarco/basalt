using System.Drawing;
using System.Windows.Forms;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Basalt.Forms.Tests.Web;

public class NativeWebBrowserHostingTests
{
    [AvaloniaFact]
    public void Browser_overlay_tracks_parent_clipping_visibility_and_position()
    {
        using var form = new Form { ClientSize = new Size(300, 200) };
        using var panel = new System.Windows.Forms.Panel { Location = new Point(20, 30), Size = new Size(100, 80) };
        using var browser = new WebBrowser { Location = new Point(70, 60), Size = new Size(80, 50) };
        panel.Controls.Add(browser);
        form.Controls.Add(panel);

        form.Show();
        try
        {
            form.Canvas!.RequestFrame();
            var view = Assert.Single(form.Canvas.GetVisualChildren().OfType<NativeWebView>());

            Assert.Equal(new Avalonia.Rect(90, 90, 80, 50), view.Bounds);
            Assert.Equal(new Avalonia.Rect(0, 0, 30, 20), Assert.IsType<RectangleGeometry>(view.Clip).Rect);

            using var covering = new System.Windows.Forms.Label
            {
                Location = new Point(75, 65),
                Size = new Size(20, 10),
            };
            panel.Controls.Add(covering);
            covering.BringToFront();
            form.Canvas.RequestFrame();
            Assert.False(view.Clip!.FillContains(new Avalonia.Point(10, 10)));
            Assert.True(view.Clip.FillContains(new Avalonia.Point(2, 2)));
            panel.Controls.Remove(covering);

            panel.Hide();
            form.Canvas.RequestFrame();
            Assert.Same(view, Assert.Single(form.Canvas.GetVisualChildren().OfType<NativeWebView>()));
            Assert.False(view.IsVisible);

            panel.Show();
            browser.Location = new Point(10, 10);
            form.Canvas.RequestFrame();
            Assert.Same(view, Assert.Single(form.Canvas.GetVisualChildren().OfType<NativeWebView>()));
            Assert.True(view.IsVisible);
            Assert.Equal(new Avalonia.Rect(30, 40, 80, 50), view.Bounds);
        }
        finally
        {
            form.Close();
        }
    }

    [AvaloniaFact]
    public void Browser_overlay_order_follows_WinForms_sibling_order()
    {
        using var form = new Form { ClientSize = new Size(300, 200) };
        using var first = new WebBrowser { Location = new Point(0, 0), Size = new Size(80, 50) };
        using var second = new WebBrowser { Location = new Point(100, 0), Size = new Size(80, 50) };
        form.Controls.Add(first);
        form.Controls.Add(second);

        form.Show();
        try
        {
            first.BringToFront();
            form.Canvas!.RequestFrame();
            Assert.Same(first.NativeView, form.Canvas.GetVisualChildren().OfType<NativeWebView>().Last());

            second.BringToFront();
            form.Canvas.RequestFrame();
            Assert.Same(second.NativeView, form.Canvas.GetVisualChildren().OfType<NativeWebView>().Last());
        }
        finally
        {
            form.Close();
        }
    }

    [AvaloniaFact]
    public void Moving_browser_subtree_between_windows_reuses_native_view()
    {
        using var first = new Form { ClientSize = new Size(300, 200) };
        using var second = new Form { ClientSize = new Size(300, 200) };
        using var panel = new System.Windows.Forms.Panel { Size = new Size(100, 80) };
        using var browser = new WebBrowser { Size = new Size(80, 50) };
        panel.Controls.Add(browser);
        first.Controls.Add(panel);

        first.Show();
        second.Show();
        try
        {
            first.Canvas!.RequestFrame();
            var view = Assert.Single(first.Canvas.GetVisualChildren().OfType<NativeWebView>());

            second.Controls.Add(panel);

            Assert.Empty(first.Canvas.GetVisualChildren().OfType<NativeWebView>());
            Assert.Same(view, Assert.Single(second.Canvas!.GetVisualChildren().OfType<NativeWebView>()));
            Assert.Same(second.Canvas, view.GetVisualParent());
        }
        finally
        {
            first.Close();
            second.Close();
        }
    }
}
