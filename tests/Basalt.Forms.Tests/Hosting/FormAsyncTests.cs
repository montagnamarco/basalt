using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Hosting;

public class FormAsyncTests
{
    [AvaloniaFact]
    public void Modeless_task_completes_after_close_and_respects_cancellation()
    {
        using var form = new Form();
        var cancel = true;
        form.FormClosing += (_, e) => e.Cancel = cancel;
        var task = form.ShowAsync();
        Assert.False(form.Visible);
        Dispatcher.UIThread.RunJobs();
        Assert.True(form.Visible);
        Assert.False(task.IsCompleted);
        form.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(task.IsCompleted);
        cancel = false;
        form.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.True(form.IsDisposed);
    }

    [AvaloniaFact]
    public void Modal_task_returns_result_without_nested_message_loop()
    {
        using var owner = new Form();
        owner.Show();
        using var form = new Form();
        var task = form.ShowDialogAsync(owner);
        Dispatcher.UIThread.RunJobs();
        Assert.True(form.Modal);
        Assert.True(form.Visible);
        Assert.False(task.IsCompleted);
        form.DialogResult = DialogResult.OK;
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(DialogResult.OK, task.Result);
        Assert.False(form.Modal);
        Assert.False(form.Visible);
        Assert.False(form.IsDisposed);
        Assert.Null(form.Owner);
    }

    [AvaloniaFact]
    public void Disposing_modal_form_finishes_task_and_allows_owner_input()
    {
        using var owner = new Form();
        owner.Show();
        var form = new Form();
        var task = form.ShowDialogAsync(owner);
        Dispatcher.UIThread.RunJobs();
        form.Dispose();
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.False(form.Modal);
        Assert.True(owner.Visible);
    }

    [AvaloniaFact]
    public void Duplicate_async_display_is_rejected_without_changing_first_task()
    {
        using var form = new Form();
        var first = form.ShowAsync();
        var second = form.ShowDialogAsync();
        Assert.True(second.IsFaulted);
        Assert.IsType<InvalidOperationException>(second.Exception!.InnerException);
        Dispatcher.UIThread.RunJobs();
        Assert.False(first.IsCompleted);
        form.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(first.IsCompletedSuccessfully);
    }

    [AvaloniaFact]
    public void Load_failure_is_propagated_and_form_can_be_shown_again()
    {
        using var form = new Form();
        EventHandler failure = (_, _) => throw new InvalidOperationException("load failed");
        form.Load += failure;
        var task = form.ShowDialogAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsFaulted);
        Assert.Equal("load failed", task.Exception!.InnerException!.Message);
        Assert.False(form.Modal);
        form.Load -= failure;
        var retry = form.ShowDialogAsync();
        Dispatcher.UIThread.RunJobs();
        form.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(retry.IsCompletedSuccessfully);
    }

    [AvaloniaFact]
    public void Failure_while_hiding_dialog_faults_task_instead_of_leaving_it_pending()
    {
        using var form = new Form();
        var task = form.ShowDialogAsync();
        Dispatcher.UIThread.RunJobs();
        form.VisibleChanged += (_, _) =>
        {
            if (!form.Visible) throw new InvalidOperationException("hide failed");
        };
        form.DialogResult = DialogResult.OK;
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsFaulted);
        Assert.Equal("hide failed", task.Exception!.InnerException!.Message);
        Assert.False(form.Modal);
    }
}
