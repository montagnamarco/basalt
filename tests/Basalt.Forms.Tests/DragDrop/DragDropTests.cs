using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DataObject = System.Windows.Forms.DataObject;
using DragDropEffects = System.Windows.Forms.DragDropEffects;

namespace Basalt.Forms.Tests.DragDrop;

/// <summary>
/// Control.DoDragDrop's modal loop: it polls the pointer the way the real OLE
/// drag loop polls native messages, so a test can drive it with ordinary
/// form.Host mouse input, scripted one step per GiveFeedback (the loop raises
/// it once per pass, whatever else did or did not change that pass).
/// </summary>
public class DragDropTests
{
    private static Form ShowWith(params Control[] controls)
    {
        var form = new Form { ClientSize = new Size(300, 100) };
        form.Controls.AddRange(controls);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    /// <summary>Pops the next scripted step, if any, each time the source asks for feedback.</summary>
    private static void Script(Control source, Queue<Action> steps) =>
        source.GiveFeedback += (_, _) =>
        {
            if (steps.Count > 0) steps.Dequeue()();
        };

    [AvaloniaFact]
    public void A_drag_enters_then_drops_on_a_target_that_allows_it_negotiating_the_effect()
    {
        var source = new Panel { Bounds = new Rectangle(0, 0, 50, 50) };
        var target = new Panel { Bounds = new Rectangle(100, 0, 50, 50), AllowDrop = true };
        var form = ShowWith(source, target);

        var events = new List<string>();
        target.DragEnter += (_, e) => { events.Add("Enter"); e.Effect = DragDropEffects.Move; };
        target.DragOver += (_, e) => { events.Add("Over"); e.Effect = DragDropEffects.Move; };
        target.DragDrop += (_, _) => events.Add("Drop");
        target.DragLeave += (_, _) => events.Add("Leave");

        var steps = new Queue<Action>();
        steps.Enqueue(() => form.Host!.MouseMove(new Avalonia.Point(125, 25), RawInputModifiers.LeftMouseButton));
        steps.Enqueue(() => form.Host!.MouseUp(new Avalonia.Point(125, 25), MouseButton.Left));
        Script(source, steps);

        form.Host!.MouseDown(new Avalonia.Point(25, 25), MouseButton.Left);
        var effect = source.DoDragDrop(new DataObject("payload"), DragDropEffects.Move | DragDropEffects.Copy);

        Assert.Equal("Enter", events[0]);
        Assert.Equal("Drop", events[^1]);
        Assert.DoesNotContain("Leave", events);
        Assert.Equal(DragDropEffects.Move, effect);

        form.Close();
    }

    [AvaloniaFact]
    public void Leaving_one_target_for_another_raises_DragLeave_before_the_next_DragEnter()
    {
        var source = new Panel { Bounds = new Rectangle(0, 0, 50, 50) };
        var first = new Panel { Bounds = new Rectangle(100, 0, 50, 50), AllowDrop = true };
        var second = new Panel { Bounds = new Rectangle(200, 0, 50, 50), AllowDrop = true };
        var form = ShowWith(source, first, second);

        var firstEvents = new List<string>();
        first.DragEnter += (_, e) => { firstEvents.Add("Enter"); e.Effect = DragDropEffects.Move; };
        first.DragLeave += (_, _) => firstEvents.Add("Leave");
        first.DragDrop += (_, _) => firstEvents.Add("Drop");

        var secondEvents = new List<string>();
        second.DragEnter += (_, e) => { secondEvents.Add("Enter"); e.Effect = DragDropEffects.Move; };
        second.DragDrop += (_, _) => secondEvents.Add("Drop");

        var steps = new Queue<Action>();
        steps.Enqueue(() => form.Host!.MouseMove(new Avalonia.Point(125, 25), RawInputModifiers.LeftMouseButton));
        steps.Enqueue(() => form.Host!.MouseMove(new Avalonia.Point(225, 25), RawInputModifiers.LeftMouseButton));
        steps.Enqueue(() => form.Host!.MouseUp(new Avalonia.Point(225, 25), MouseButton.Left));
        Script(source, steps);

        form.Host!.MouseDown(new Avalonia.Point(25, 25), MouseButton.Left);
        source.DoDragDrop(new DataObject("payload"), DragDropEffects.Move);

        Assert.Equal(["Enter", "Leave"], firstEvents);
        Assert.Equal("Enter", secondEvents[0]);
        Assert.Contains("Drop", secondEvents);

        form.Close();
    }

    [AvaloniaFact]
    public void A_control_that_does_not_allow_drop_never_sees_the_drag()
    {
        var source = new Panel { Bounds = new Rectangle(0, 0, 50, 50) };
        var notATarget = new Panel { Bounds = new Rectangle(100, 0, 50, 50) };
        var form = ShowWith(source, notATarget);

        var sawDragEnter = false;
        notATarget.DragEnter += (_, _) => sawDragEnter = true;

        var steps = new Queue<Action>();
        steps.Enqueue(() => form.Host!.MouseMove(new Avalonia.Point(125, 25), RawInputModifiers.LeftMouseButton));
        steps.Enqueue(() => form.Host!.MouseUp(new Avalonia.Point(125, 25), MouseButton.Left));
        Script(source, steps);

        form.Host!.MouseDown(new Avalonia.Point(25, 25), MouseButton.Left);
        var effect = source.DoDragDrop(new DataObject("payload"), DragDropEffects.Move);

        Assert.False(sawDragEnter);
        Assert.Equal(DragDropEffects.None, effect);

        form.Close();
    }

    [AvaloniaFact]
    public void Escape_cancels_the_drag_through_QueryContinueDrag()
    {
        var source = new Panel { Bounds = new Rectangle(0, 0, 50, 50) };
        var target = new Panel { Bounds = new Rectangle(100, 0, 50, 50), AllowDrop = true };
        var form = ShowWith(source, target);

        var actions = new List<DragAction>();
        source.QueryContinueDrag += (_, e) => actions.Add(e.Action);
        var dropped = false;
        target.DragDrop += (_, _) => dropped = true;

        var steps = new Queue<Action>();
        steps.Enqueue(() => form.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None));
        Script(source, steps);

        form.Host!.MouseDown(new Avalonia.Point(25, 25), MouseButton.Left);
        var effect = source.DoDragDrop(new DataObject("payload"), DragDropEffects.Move);

        Assert.Equal(DragAction.Cancel, actions[^1]);
        Assert.Equal(DragDropEffects.None, effect);
        Assert.False(dropped);

        // Clean up the simulated input so it does not leak into another test.
        form.Host!.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        form.Host!.MouseUp(new Avalonia.Point(25, 25), MouseButton.Left);
        form.Close();
    }

    [AvaloniaFact]
    public void Escape_during_a_drag_does_not_reach_the_forms_cancel_button()
    {
        var source = new Panel { Bounds = new Rectangle(0, 0, 50, 50) };
        var cancel = new Button { Bounds = new Rectangle(200, 60, 60, 23) };
        var form = ShowWith(source, cancel);
        form.CancelButton = cancel;
        var cancelClicks = 0;
        cancel.Click += (_, _) => cancelClicks++;

        var steps = new Queue<Action>();
        steps.Enqueue(() => form.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None));
        Script(source, steps);

        form.Host!.MouseDown(new Avalonia.Point(25, 25), MouseButton.Left);
        var effect = source.DoDragDrop(new DataObject("payload"), DragDropEffects.Move);

        Assert.Equal(DragDropEffects.None, effect);
        Assert.Equal(0, cancelClicks);

        form.Host!.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        form.Host!.MouseUp(new Avalonia.Point(25, 25), MouseButton.Left);
        form.Close();
    }
}
