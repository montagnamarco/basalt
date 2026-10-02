using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Core;

/// <summary>Message filters, Idle, the message loop callback a host without its own registers, and the simple settings.</summary>
public class ApplicationMessageLoopTests
{
    private sealed class RecordingFilter(bool consume) : IMessageFilter
    {
        public int Seen { get; private set; }

        public bool PreFilterMessage(ref Message m)
        {
            Seen++;
            return consume;
        }
    }

    [Fact]
    public void FilterMessage_offers_the_message_to_every_filter_until_one_consumes_it()
    {
        var first = new RecordingFilter(consume: false);
        var second = new RecordingFilter(consume: true);
        var third = new RecordingFilter(consume: true);

        Application.AddMessageFilter(first);
        Application.AddMessageFilter(second);
        Application.AddMessageFilter(third);

        try
        {
            var message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
            var consumed = Application.FilterMessage(ref message);

            Assert.True(consumed);
            Assert.Equal(1, first.Seen);
            Assert.Equal(1, second.Seen);
            Assert.Equal(0, third.Seen);
        }
        finally
        {
            Application.RemoveMessageFilter(first);
            Application.RemoveMessageFilter(second);
            Application.RemoveMessageFilter(third);
        }
    }

    [Fact]
    public void RemoveMessageFilter_stops_it_from_seeing_further_messages()
    {
        var filter = new RecordingFilter(consume: false);
        Application.AddMessageFilter(filter);
        Application.RemoveMessageFilter(filter);

        var message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        Application.FilterMessage(ref message);

        Assert.Equal(0, filter.Seen);
    }

    [Fact]
    public void RaiseIdle_invokes_the_Idle_event()
    {
        var raised = 0;
        EventHandler handler = (_, _) => raised++;

        Application.Idle += handler;

        try
        {
            Application.RaiseIdle(EventArgs.Empty);
            Assert.Equal(1, raised);
        }
        finally
        {
            Application.Idle -= handler;
        }
    }

    [AvaloniaFact]
    public void Idle_arrives_on_its_own_while_Run_is_pumping_messages()
    {
        var raised = 0;

        void OnIdle(object? sender, EventArgs e)
        {
            raised++;
            Application.ExitThread();
        }

        Application.Idle += OnIdle;

        try
        {
            // Nobody calls RaiseIdle here: entering Run must schedule it, or this never returns.
            Application.Run();

            Assert.Equal(1, raised);
        }
        finally
        {
            Application.Idle -= OnIdle;
        }
    }

    [AvaloniaFact]
    public void Idle_arrives_on_its_own_while_ShowDialog_is_active()
    {
        var dialog = new Form();
        var raised = 0;

        void OnIdle(object? sender, EventArgs e)
        {
            raised++;
            dialog.DialogResult = DialogResult.OK;
        }

        Application.Idle += OnIdle;

        try
        {
            // Nobody calls RaiseIdle here either: entering ShowDialog's modal loop must schedule it.
            var result = dialog.ShowDialog();

            Assert.Equal(1, raised);
            Assert.Equal(DialogResult.OK, result);
        }
        finally
        {
            Application.Idle -= OnIdle;
            dialog.Dispose();
        }
    }

    [AvaloniaFact]
    public void ScheduleIdle_raises_Idle_once_and_pumping_without_further_input_raises_nothing_more()
    {
        var raised = 0;
        EventHandler handler = (_, _) => raised++;
        Application.Idle += handler;

        try
        {
            Application.ScheduleIdle();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, raised);

            // (c) With nothing new scheduling it, pumping the queue for a while raises no more.
            var until = DateTime.UtcNow.AddMilliseconds(200);
            while (DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, raised);
        }
        finally
        {
            Application.Idle -= handler;
        }
    }

    [AvaloniaFact]
    public void A_simulated_input_message_schedules_exactly_one_more_Idle()
    {
        var raised = 0;
        EventHandler handler = (_, _) => raised++;
        Application.Idle += handler;

        try
        {
            Application.ScheduleIdle();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, raised);

            // Every mouse and keyboard message passes through FormsInput.Guard; simulate one
            // the way production code dispatches a real key or pointer message through it.
            var window = new Control();
            FormsInput.Guard(window, () => { });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, raised);

            // The message is done being processed: nothing schedules a second Idle for it.
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, raised);
        }
        finally
        {
            Application.Idle -= handler;
        }
    }

    [AvaloniaFact]
    public void Run_installs_the_context_while_active_and_restores_the_previous_one_once_it_returns()
    {
        var sentinel = new SynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(sentinel);

        try
        {
            SynchronizationContext? duringRun = null;
            var form = new Form();

            // Load runs synchronously inside Run, before the dispatcher's own main loop (and its
            // own AvaloniaSynchronizationContext for dispatched callbacks) is ever entered.
            form.Load += (_, _) =>
            {
                duringRun = SynchronizationContext.Current;
                Application.ExitThread();
            };

            Application.Run(form);

            Assert.IsType<WindowsFormsSynchronizationContext>(duringRun);
            Assert.Same(sentinel, SynchronizationContext.Current);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [AvaloniaFact]
    public void A_registered_message_loop_callback_is_pumped_by_DoEvents()
    {
        var calls = 0;
        bool Loop()
        {
            calls++;
            return calls < 3;
        }

        Application.RegisterMessageLoop(Loop);

        try
        {
            Application.DoEvents();
            Assert.Equal(3, calls);
        }
        finally
        {
            Application.UnregisterMessageLoop();
        }

        // Unregistered, DoEvents no longer calls it.
        Application.DoEvents();
        Assert.Equal(3, calls);
    }

    [Fact]
    public void SetColorMode_is_read_back_from_both_ColorMode_and_SystemColorMode()
    {
        var previous = Application.ColorMode;

        try
        {
            Application.SetColorMode(SystemColorMode.Dark);

            Assert.Equal(SystemColorMode.Dark, Application.ColorMode);
            Assert.Equal(SystemColorMode.Dark, Application.SystemColorMode);
        }
        finally
        {
            Application.SetColorMode(previous);
        }
    }

    [Fact]
    public void SafeTopLevelCaptionFormat_round_trips_and_rejects_null_back_to_the_default()
    {
        var previous = Application.SafeTopLevelCaptionFormat;

        try
        {
            Application.SafeTopLevelCaptionFormat = "{0} (restricted)";
            Assert.Equal("{0} (restricted)", Application.SafeTopLevelCaptionFormat);

            Application.SafeTopLevelCaptionFormat = null!;
            Assert.Equal("{0}", Application.SafeTopLevelCaptionFormat);
        }
        finally
        {
            Application.SafeTopLevelCaptionFormat = previous;
        }
    }
}
