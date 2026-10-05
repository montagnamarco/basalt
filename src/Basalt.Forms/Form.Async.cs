using Avalonia.Threading;

namespace System.Windows.Forms;

public partial class Form
{
    private TaskCompletionSource<DialogResult>? _asyncDisplay;

    public Task ShowAsync(IWin32Window? owner = null) => StartAsyncDisplay(owner, modal: false);

    public Task<DialogResult> ShowDialogAsync() => ShowDialogAsync(null);

    public Task<DialogResult> ShowDialogAsync(IWin32Window? owner) => StartAsyncDisplay(owner, modal: true);

    private Task<DialogResult> StartAsyncDisplay(IWin32Window? owner, bool modal)
    {
        var completion = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _asyncDisplay, completion, null) is not null)
        {
            return Task.FromException<DialogResult>(new InvalidOperationException("The form is already displayed asynchronously."));
        }

        // Posting also makes a large form's initialization asynchronous for
        // callers on the UI thread; no nested dispatcher frame is needed.
        Dispatcher.UIThread.Post(() => BeginAsyncDisplay(owner, modal, completion));
        return completion.Task;
    }

    private void BeginAsyncDisplay(IWin32Window? owner, bool modal, TaskCompletionSource<DialogResult> completion)
    {
        Form? previousOwner = _owner;
        Form? ownerForm = owner as Form ?? ActiveForm;
        List<Form> disabled = [];
        var startedModal = false;
        var finished = false;

        void Finish(Exception? error = null)
        {
            if (finished) return;
            finished = true;
            FormClosed -= Closed;
            Disposed -= DisposedHandler;

            void Cleanup(Action action)
            {
                try { action(); }
                catch (Exception cleanupError) { error ??= cleanupError; }
            }

            if (startedModal)
            {
                _modal = false;
                foreach (var form in disabled)
                {
                    if (!form.IsDisposed) Cleanup(() => form.SetHostEnabled(true));
                }

                if (!IsDisposed) Cleanup(() => Visible = false);
                if (!IsDisposed && _owner != previousOwner) Cleanup(() => Owner = previousOwner);
                if (ownerForm is { IsDisposed: false }) Cleanup(ownerForm.Activate);
            }

            Interlocked.CompareExchange(ref _asyncDisplay, null, completion);
            if (error is not null) completion.TrySetException(error);
            else completion.TrySetResult(_dialogResult);
        }

        void Closed(object? sender, FormClosedEventArgs e) => Dispatcher.UIThread.Post(() => Finish());
        void DisposedHandler(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => Finish());

        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (owner == this) throw new ArgumentException("Forms cannot own themselves.", nameof(owner));
            if (Visible || _modal) throw new InvalidOperationException("The form is already displayed.");
            if (modal && (!Enabled || !TopLevel)) throw new InvalidOperationException("A modal form must be enabled and top-level.");

            FormClosed += Closed;
            Disposed += DisposedHandler;

            if (modal)
            {
                if (ownerForm == this) ownerForm = null;
                if (ownerForm is not null && _owner is null) Owner = ownerForm;
                _modal = true;
                startedModal = true;
                _dialogResult = DialogResult.None;
                disabled = DisableOtherForms();
                Visible = true;
            }
            else
            {
                Show(owner);
            }
        }
        catch (Exception error)
        {
            Finish(error);
        }
    }
}
