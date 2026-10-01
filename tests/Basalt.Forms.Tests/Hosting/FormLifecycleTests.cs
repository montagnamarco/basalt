using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Hosting;

/// <summary>Showing, closing and modal forms, on the headless platform.</summary>
public class FormLifecycleTests
{
    private sealed class RecordingForm : Form
    {
        public List<string> Log { get; } = [];

        protected override void OnHandleCreated(EventArgs e)
        {
            Log.Add("HandleCreated");
            base.OnHandleCreated(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            Log.Add("Load");
            base.OnLoad(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            Log.Add($"VisibleChanged({Visible})");
            base.OnVisibleChanged(e);
        }

        protected override void OnShown(EventArgs e)
        {
            Log.Add("Shown");
            base.OnShown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Log.Add($"FormClosing({e.CloseReason})");
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Log.Add($"FormClosed({e.CloseReason})");
            base.OnFormClosed(e);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Log.Add("HandleDestroyed");
            base.OnHandleDestroyed(e);
        }
    }

    [AvaloniaFact]
    public void Show_creates_the_handle_loads_once_and_posts_Shown()
    {
        var form = new RecordingForm();

        form.Show();
        Assert.Equal(["HandleCreated", "Load", "VisibleChanged(True)"], form.Log);
        Assert.Contains(form, Application.OpenForms.Cast<Form>());

        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Shown", form.Log[^1]);

        form.Hide();
        form.Show();
        Assert.Single(form.Log, entry => entry == "Load");

        form.Close();
    }

    [AvaloniaFact]
    public void Close_raises_closing_and_closed_then_disposes()
    {
        var form = new RecordingForm();
        form.Show();
        form.Log.Clear();

        form.Close();

        Assert.Equal(["FormClosing(UserClosing)", "FormClosed(UserClosing)", "HandleDestroyed"], form.Log);
        Assert.True(form.IsDisposed);
        Assert.DoesNotContain(form, Application.OpenForms.Cast<Form>());
    }

    [AvaloniaFact]
    public void A_cancelled_FormClosing_keeps_the_form_open()
    {
        var form = new Form();
        form.FormClosing += (_, e) => e.Cancel = true;
        form.Show();

        form.Close();

        Assert.False(form.IsDisposed);
        Assert.True(form.Visible);

        form.Dispose();
    }

    [AvaloniaFact]
    public void Closing_a_form_that_was_never_shown_disposes_it()
    {
        var form = new RecordingForm();

        form.Close();

        Assert.True(form.IsDisposed);
        Assert.Empty(form.Log);
    }

    [AvaloniaFact]
    public void ShowDialog_waits_until_DialogResult_is_set_and_returns_it()
    {
        var dialog = new RecordingForm();
        dialog.Shown += (_, _) => dialog.DialogResult = DialogResult.OK;

        var result = dialog.ShowDialog();

        Assert.Equal(DialogResult.OK, result);
        Assert.False(dialog.Visible);
        Assert.False(dialog.IsDisposed);
        Assert.Contains("FormClosing(None)", dialog.Log);

        dialog.Dispose();
    }

    [AvaloniaFact]
    public void Nested_dialogs_return_in_order()
    {
        var outer = new Form();
        var inner = new Form();
        var order = new List<string>();

        outer.Shown += (_, _) =>
        {
            inner.Shown += (_, _) => inner.DialogResult = DialogResult.Yes;
            order.Add($"inner {inner.ShowDialog(outer)}");
            outer.DialogResult = DialogResult.No;
        };

        order.Add($"outer {outer.ShowDialog()}");

        Assert.Equal(["inner Yes", "outer No"], order);

        inner.Dispose();
        outer.Dispose();
    }

    [AvaloniaFact]
    public void Closing_a_modal_form_without_a_result_answers_Cancel()
    {
        var dialog = new Form();
        dialog.Shown += (_, _) => dialog.Close();

        Assert.Equal(DialogResult.Cancel, dialog.ShowDialog());

        dialog.Dispose();
    }

    [AvaloniaFact]
    public void A_modal_form_closed_in_Load_returns_at_once_and_never_shows()
    {
        var dialog = new RecordingForm();
        dialog.Load += (_, _) => dialog.Close();

        var result = dialog.ShowDialog();

        Assert.Equal(DialogResult.Cancel, result);
        Assert.DoesNotContain("VisibleChanged(True)", dialog.Log);
        Assert.DoesNotContain(dialog, Application.OpenForms.Cast<Form>());

        dialog.Dispose();
    }

    [AvaloniaFact]
    public void A_form_that_is_visible_cannot_be_shown_modally()
    {
        var form = new Form();
        form.Show();

        Assert.Throws<InvalidOperationException>(() => form.ShowDialog());

        form.Close();
    }

    [AvaloniaFact]
    public void The_window_shows_what_the_controls_paint()
    {
        var form = new Form { ClientSize = new Size(100, 60), BackColor = Color.White };
        _ = new Control { Bounds = new Rectangle(10, 10, 30, 20), BackColor = Color.Blue, Parent = form };
        form.Show();

        using var frame = form.Host!.CaptureRenderedFrame()!;
        using var locked = frame.Lock();

        Assert.Equal(Color.Blue.ToArgb(), PixelAt(locked, 20, 15));
        Assert.Equal(Color.White.ToArgb(), PixelAt(locked, 60, 40));

        form.Close();
    }

    private static int PixelAt(Avalonia.Platform.ILockedFramebuffer buffer, int x, int y)
    {
        // The frame may be rendered at a scale; the logical point is scaled into it.
        var scale = buffer.Size.Width / 100.0;
        var px = (int)(x * scale);
        var py = (int)(y * scale);

        unsafe
        {
            var row = (byte*)buffer.Address + py * buffer.RowBytes;
            var pixel = row + px * 4;

            return buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888
                ? Color.FromArgb(pixel[3], pixel[0], pixel[1], pixel[2]).ToArgb()
                : Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]).ToArgb();
        }
    }
}
