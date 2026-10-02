using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Core;

/// <summary>
/// The members of Control that stand for Windows machinery: the window
/// procedure, the window region, mouse hover, IME mode, data context,
/// lock keys and right-to-left translation.
/// </summary>
public class ControlPlatformTests
{
    [AvaloniaFact]
    public void A_WndProc_override_sees_the_key_message_after_PreProcessMessage_and_can_swallow_it()
    {
        var box = new SwallowingControl { Bounds = new Rectangle(0, 0, 50, 20) };
        var form = Show(box);
        box.Focus();
        var keyDowns = 0;
        box.KeyDown += (_, _) => keyDowns++;

        form.Host!.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);
        box.Swallow = true;
        form.Host!.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);

        Assert.Equal(1, keyDowns);
        Assert.Contains(0x0100, box.Messages);
        Assert.Equal(["PreProcess", "WndProc"], box.Order.Take(2));

        form.Close();
    }

    [AvaloniaFact]
    public void ReflectMessage_hands_the_notification_back_to_the_control_it_names_with_the_reflect_offset()
    {
        using var target = new SwallowingControl();
        var message = Message.Create(IntPtr.Zero, 0x0111, (IntPtr)7, IntPtr.Zero);

        var reflected = SwallowingControl.Reflect(target.Handle, ref message);
        var unknown = SwallowingControl.Reflect((IntPtr)12345, ref message);

        Assert.True(reflected);
        Assert.False(unknown);
        Assert.Equal(0x2000 + 0x0111, Assert.Single(target.Messages));
    }

    [AvaloniaFact]
    public void A_replaced_WindowTarget_receives_the_messages_instead_of_WndProc()
    {
        var box = new SwallowingControl { Bounds = new Rectangle(0, 0, 50, 20) };
        var form = Show(box);
        box.Focus();
        var target = new RecordingTarget();
        box.WindowTarget = target;

        form.Host!.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);

        Assert.Contains(0x0100, target.Messages);
        Assert.DoesNotContain(0x0100, box.Messages);

        form.Close();
    }

    [AvaloniaFact]
    public void A_control_is_neither_hit_nor_drawn_outside_its_region()
    {
        var parent = new Control { BackColor = Color.White, Size = new Size(40, 40) };
        var child = new Control { BackColor = Color.Red, Bounds = new Rectangle(0, 0, 40, 40) };
        parent.Controls.Add(child);
        var changes = 0;
        child.RegionChanged += (_, _) => changes++;

        child.Region = new Region(new Rectangle(0, 0, 20, 40));

        using var bitmap = new Bitmap(40, 40);
        parent.DrawToBitmap(bitmap, new Rectangle(0, 0, 40, 40));

        Assert.Equal(1, changes);
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(10, 20).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(30, 20).ToArgb());
        Assert.Same(child, parent.GetChildAtPoint(new Point(10, 20)));
        Assert.Null(parent.GetChildAtPoint(new Point(30, 20)));
    }

    [AvaloniaFact]
    public void MouseHover_comes_once_per_entry_until_ResetMouseEventArgs_arms_it_again()
    {
        var box = new SwallowingControl { Bounds = new Rectangle(0, 0, 50, 50) };
        var form = Show(box);
        var hovers = 0;
        box.MouseHover += (_, _) => hovers++;

        form.Host!.MouseMove(new Avalonia.Point(10, 10));
        MouseHoverTracker.Elapse();
        MouseHoverTracker.Elapse();
        Assert.Equal(1, hovers);

        box.Rearm();
        MouseHoverTracker.Elapse();
        Assert.Equal(2, hovers);

        form.Close();
    }

    [AvaloniaFact]
    public void ImeMode_inherits_from_the_parent_and_reports_its_changes()
    {
        var parent = new Control();
        var child = new Control();
        parent.Controls.Add(child);
        var changes = 0;
        child.ImeModeChanged += (_, _) => changes++;

        Assert.Equal(ImeMode.NoControl, child.ImeMode);

        parent.ImeMode = ImeMode.Hiragana;
        child.ImeMode = ImeMode.OnHalf;

        Assert.Equal(ImeMode.Hiragana, parent.ImeMode);
        Assert.Equal(ImeMode.On, child.ImeMode);
        Assert.Equal(1, changes);
        Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => child.ImeMode = (ImeMode)99);
    }

    [AvaloniaFact]
    public void DataContext_flows_to_children_until_one_has_its_own()
    {
        var parent = new Control();
        var child = new Control();
        var grandchild = new Control();
        child.Controls.Add(grandchild);
        var raised = new List<string>();
        child.DataContextChanged += (_, _) => raised.Add("child");
        grandchild.DataContextChanged += (_, _) => raised.Add("grandchild");

        parent.DataContext = "model";
        parent.Controls.Add(child);

        Assert.Equal("model", grandchild.DataContext);
        Assert.Equal(["child", "grandchild"], raised);

        child.DataContext = "own";
        parent.DataContext = "other";
        Assert.Equal("own", grandchild.DataContext);

        child.DataContext = null;
        Assert.Equal("other", grandchild.DataContext);
    }

    [AvaloniaFact]
    public void IsKeyLocked_follows_the_presses_of_the_lock_keys_and_rejects_other_keys()
    {
        var form = Show(new Control());
        var before = Control.IsKeyLocked(Keys.CapsLock);

        form.Host!.KeyPressQwerty(PhysicalKey.CapsLock, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.CapsLock, RawInputModifiers.None);

        Assert.Equal(!before, Control.IsKeyLocked(Keys.CapsLock));
        Assert.Throws<NotSupportedException>(() => Control.IsKeyLocked(Keys.A));

        form.Host!.KeyPressQwerty(PhysicalKey.CapsLock, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.CapsLock, RawInputModifiers.None);
        Assert.Equal(before, Control.IsKeyLocked(Keys.CapsLock));

        form.Close();
    }

    [AvaloniaFact]
    public void Alignments_are_mirrored_only_from_right_to_left()
    {
        var control = new SwallowingControl();

        Assert.Equal(ContentAlignment.TopLeft, control.Translate(ContentAlignment.TopLeft));

        control.RightToLeft = RightToLeft.Yes;

        Assert.Equal(ContentAlignment.TopRight, control.Translate(ContentAlignment.TopLeft));
        Assert.Equal(ContentAlignment.MiddleCenter, control.Translate(ContentAlignment.MiddleCenter));
        Assert.Equal(HorizontalAlignment.Left, control.Translate(HorizontalAlignment.Right));
    }

    [AvaloniaFact]
    public void OnPrint_paints_the_background_and_the_foreground_into_the_graphics_it_is_given()
    {
        var control = new SwallowingControl { BackColor = Color.Blue, Size = new Size(10, 10) };
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);

        control.Print(graphics);

        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(5, 5).ToArgb());
    }

    private static Form Show(Control content)
    {
        var form = new Form { ClientSize = new Size(100, 100) };
        form.Controls.Add(content);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private sealed class SwallowingControl : Control
    {
        public SwallowingControl() => SetStyle(ControlStyles.Selectable, true);

        public bool Swallow { get; set; }

        public List<int> Messages { get; } = [];

        public List<string> Order { get; } = [];

        public static bool Reflect(IntPtr handle, ref Message message) => ReflectMessage(handle, ref message);

        public void Rearm() => ResetMouseEventArgs();

        public ContentAlignment Translate(ContentAlignment alignment) => RtlTranslateAlignment(alignment);

        public HorizontalAlignment Translate(HorizontalAlignment alignment) => RtlTranslateAlignment(alignment);

        public void Print(Graphics graphics) => OnPrint(new PaintEventArgs(graphics, ClientRectangle));

        public override bool PreProcessMessage(ref Message msg)
        {
            Order.Add("PreProcess");
            return base.PreProcessMessage(ref msg);
        }

        protected override void WndProc(ref Message m)
        {
            Order.Add("WndProc");
            Messages.Add(m.Msg);

            if (Swallow) return;

            base.WndProc(ref m);
        }
    }

    private sealed class RecordingTarget : IWindowTarget
    {
        public List<int> Messages { get; } = [];

        public void OnHandleChange(IntPtr newHandle)
        {
        }

        public void OnMessage(ref Message m) => Messages.Add(m.Msg);
    }
}
