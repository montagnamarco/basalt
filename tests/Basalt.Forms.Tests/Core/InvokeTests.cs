using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Core;

/// <summary>
/// Reaching a control from another thread: Invoke, BeginInvoke/EndInvoke
/// and InvokeAsync run the call on the UI thread in WinForms' order.
/// </summary>
public class InvokeTests
{
    [AvaloniaFact]
    public void Invoke_is_required_only_from_another_thread()
    {
        using var control = ControlWithHandle();

        var fromWorker = SynchronousWait.For(Task.Run(() => control.InvokeRequired));

        Assert.False(control.InvokeRequired);
        Assert.True(fromWorker);
    }

    [AvaloniaFact]
    public void A_control_with_no_handle_in_its_chain_cannot_be_invoked()
    {
        using var orphan = new Control();

        Assert.False(orphan.InvokeRequired);
        Assert.Throws<InvalidOperationException>(() => orphan.BeginInvoke(() => { }));
        Assert.Throws<InvalidOperationException>(() => orphan.Invoke(() => { }));
    }

    [AvaloniaFact]
    public void Invoke_from_a_worker_runs_on_the_UI_thread_and_returns_the_value()
    {
        using var control = ControlWithHandle();
        var uiThread = Environment.CurrentManagedThreadId;

        var (callThread, value) = SynchronousWait.For(Task.Run(() =>
        {
            var thread = control.Invoke(() => Environment.CurrentManagedThreadId);
            var sum = control.Invoke(new Func<int, int, int>((a, b) => a + b), 2, 3);

            return (thread, (int)sum!);
        }));

        Assert.Equal(uiThread, callThread);
        Assert.Equal(5, value);
    }

    [AvaloniaFact]
    public void An_exception_in_Invoke_reaches_the_worker_that_called()
    {
        using var control = ControlWithHandle();

        var thrown = SynchronousWait.For(Task.Run(() =>
            Record.Exception(() => control.Invoke(new Action(() => throw new FormatException("bad"))))));

        var format = Assert.IsType<FormatException>(thrown);
        Assert.Equal("bad", format.Message);
    }

    [AvaloniaFact]
    public void Invoke_on_the_UI_thread_first_runs_the_calls_already_queued()
    {
        using var control = ControlWithHandle();
        var order = new List<string>();

        control.BeginInvoke(() => order.Add("queued"));
        control.Invoke(() => order.Add("invoked"));

        Assert.Equal(["queued", "invoked"], order);
    }

    [AvaloniaFact]
    public void EndInvoke_on_the_UI_thread_runs_the_call_instead_of_waiting_for_it()
    {
        using var control = ControlWithHandle();

        var result = control.BeginInvoke(new Func<string>(() => "done"));

        Assert.False(result.IsCompleted);
        Assert.Equal("done", control.EndInvoke(result));
    }

    [AvaloniaFact]
    public void BeginInvoke_runs_later_on_the_message_loop()
    {
        using var control = ControlWithHandle();
        var ran = false;

        control.BeginInvoke(() => ran = true);
        Assert.False(ran);

        Application.DoEvents();
        Assert.True(ran);
    }

    [AvaloniaFact]
    public void A_call_left_waiting_when_the_handle_is_destroyed_fails_with_ObjectDisposedException()
    {
        var control = ControlWithHandle();
        var ran = false;

        var result = control.BeginInvoke(() => ran = true);
        control.Dispose();

        // Completed at once: a worker blocked in Invoke must not wait for a message loop that may have ended.
        Assert.True(result.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => control.EndInvoke(result));
        Application.DoEvents();
        Assert.False(ran);
    }

    [AvaloniaFact]
    public void EndInvoke_rejects_a_result_from_another_control()
    {
        using var first = ControlWithHandle();
        using var second = ControlWithHandle();

        var result = first.BeginInvoke(() => { });

        Assert.Throws<ArgumentException>(() => second.EndInvoke(result));
        first.EndInvoke(result);
    }

    [AvaloniaFact]
    public void InvokeAsync_completes_with_the_value_computed_on_the_UI_thread()
    {
        using var control = ControlWithHandle();
        var uiThread = Environment.CurrentManagedThreadId;

        var thread = SynchronousWait.For(Task.Run(() => control.InvokeAsync(() => Environment.CurrentManagedThreadId)));

        Assert.Equal(uiThread, thread);
    }

    [AvaloniaFact]
    public void InvokeAsync_with_an_async_callback_resumes_on_the_UI_thread_after_awaiting()
    {
        using var control = ControlWithHandle();
        var uiThread = Environment.CurrentManagedThreadId;

        var thread = SynchronousWait.For(Task.Run(() => control.InvokeAsync(async token =>
        {
            await Task.Delay(10, token);
            return Environment.CurrentManagedThreadId;
        })));

        Assert.Equal(uiThread, thread);
    }

    [AvaloniaFact]
    public void InvokeAsync_with_a_cancelled_token_never_runs_the_callback()
    {
        using var control = ControlWithHandle();
        var ran = false;

        var task = control.InvokeAsync(() => ran = true, new CancellationToken(canceled: true));
        Application.DoEvents();

        Assert.True(task.IsCanceled);
        Assert.False(ran);
    }

    [AvaloniaFact]
    public void The_handle_from_another_thread_throws_while_cross_thread_checks_are_on()
    {
        using var control = ControlWithHandle();
        var previous = Control.CheckForIllegalCrossThreadCalls;

        try
        {
            Control.CheckForIllegalCrossThreadCalls = true;

            var thrown = SynchronousWait.For(Task.Run(() => Record.Exception(() => control.Handle)));

            Assert.IsType<InvalidOperationException>(thrown);
            Assert.NotEqual(IntPtr.Zero, control.Handle);
        }
        finally
        {
            Control.CheckForIllegalCrossThreadCalls = previous;
        }
    }

    private static Control ControlWithHandle()
    {
        var control = new Control();
        _ = control.Handle;

        return control;
    }
}
