using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>
/// Which thread a control belongs to, and how other threads reach it:
/// Invoke, BeginInvoke and InvokeAsync queue a call on the control and post
/// to the UI thread, as WinForms posts its private thread-callback message.
/// </summary>
/// <remarks>
/// The queue is the control's own, not the dispatcher's, because WinForms
/// code depends on its order: a synchronous Invoke made on the UI thread
/// first runs the BeginInvoke calls still waiting on that control, and
/// EndInvoke on the UI thread runs them instead of waiting for a message
/// that could only arrive after it returns.
/// </remarks>
public partial class Control : ISynchronizeInvoke
{
    private const string NoHandleMessage =
        "Invoke or BeginInvoke cannot be called on a control until the window handle has been created.";

    private int _owningThreadId;

    private readonly Lock _invokeGate = new();
    private Queue<InvokeEntry>? _invokeQueue;
    private bool _invokePosted;

    /// <summary>
    /// Whether touching a control's handle from another thread throws, which
    /// WinForms turns on while a debugger is attached so the mistake shows
    /// where it is made.
    /// </summary>
    public static bool CheckForIllegalCrossThreadCalls { get; set; } = Debugger.IsAttached;

    /// <summary>
    /// Whether the caller is on another thread than the control's; false
    /// when neither the control nor a parent has a handle yet, because
    /// there is then no thread to compare with.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool InvokeRequired
    {
        get
        {
            var marshaler = FindMarshalingControl();

            return marshaler.IsHandleCreated && marshaler._owningThreadId != Environment.CurrentManagedThreadId;
        }
    }

    public IAsyncResult BeginInvoke(Action method) => BeginInvoke(method, null);

    public IAsyncResult BeginInvoke(Delegate method) => BeginInvoke(method, null);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public IAsyncResult BeginInvoke(Delegate method, params object?[]? args) => MarshaledInvoke(method, args, synchronous: false);

    public object? EndInvoke(IAsyncResult asyncResult)
    {
        ArgumentNullException.ThrowIfNull(asyncResult);

        if (asyncResult is not InvokeEntry entry) throw new ArgumentException("The IAsyncResult object was not returned from the corresponding asynchronous method on this class.", nameof(asyncResult));
        if (entry.Caller != this) throw new ArgumentException("The IAsyncResult object was not returned from the corresponding asynchronous method on this class.", nameof(asyncResult));

        if (!entry.IsCompleted)
        {
            // On the UI thread the posted callback could only run after this returns: run the queue now.
            if (entry.Marshaler._owningThreadId == Environment.CurrentManagedThreadId) entry.Marshaler.RunInvokeQueue();
            else entry.AsyncWaitHandle.WaitOne();
        }

        if (entry.Exception is { } exception) throw exception;

        return entry.ReturnValue;
    }

    public void Invoke(Action method) => Invoke(method, null);

    public object? Invoke(Delegate method) => Invoke(method, null);

    public object? Invoke(Delegate method, params object?[]? args)
    {
        var entry = (InvokeEntry)MarshaledInvoke(method, args, synchronous: true);

        if (entry.Exception is { } exception) throw exception;

        return entry.ReturnValue;
    }

    public T Invoke<T>(Func<T> method) => (T)Invoke(method, null)!;

    /// <summary>
    /// Runs the callback on the UI thread and completes when it has run; a
    /// token cancelled before the callback starts cancels it.
    /// </summary>
    public Task InvokeAsync(Action callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return InvokeAsync<object?>(() =>
        {
            callback();
            return null;
        }, cancellationToken);
    }

    public Task<T> InvokeAsync<T>(Func<T> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return InvokeAsync<T>(_ => ValueTask.FromResult(callback()), cancellationToken);
    }

    public Task InvokeAsync(Func<CancellationToken, ValueTask> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return InvokeAsync<object?>(async token =>
        {
            await callback(token).ConfigureAwait(true);
            return null;
        }, cancellationToken);
    }

    /// <summary>
    /// The general form: the callback starts on the UI thread and its
    /// awaits come back there through the UI thread's synchronization context.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<CancellationToken, ValueTask<T>> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken), useSynchronizationContext: false);

        async void Run()
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                completion.TrySetResult(await callback(cancellationToken).ConfigureAwait(true));
            }
            catch (OperationCanceledException canceled) when (canceled.CancellationToken == cancellationToken)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                registration.Dispose();
            }
        }

        try
        {
            BeginInvoke((Action)Run);
        }
        catch
        {
            registration.Dispose();
            throw;
        }

        return completion.Task;
    }

    /// <summary>
    /// The control whose thread a call is marshalled to: this one once it has
    /// a handle, else the nearest parent with one, else the top of the chain.
    /// </summary>
    private Control FindMarshalingControl()
    {
        var control = this;

        while (!control.IsHandleCreated && control._parent is { } parent) control = parent;

        return control.IsHandleCreated ? control : this;
    }

    private IAsyncResult MarshaledInvoke(Delegate method, object?[]? args, bool synchronous)
    {
        ArgumentNullException.ThrowIfNull(method);

        var marshaler = FindMarshalingControl();
        if (!marshaler.IsHandleCreated) throw new InvalidOperationException(NoHandleMessage);

        var sameThread = marshaler._owningThreadId == Environment.CurrentManagedThreadId;

        // Another thread's call runs with that thread's culture, principal and async locals, as in WinForms.
        var context = sameThread ? null : ExecutionContext.Capture();
        var entry = new InvokeEntry(this, marshaler, method, args, synchronous, context);

        lock (marshaler._invokeGate)
        {
            (marshaler._invokeQueue ??= new()).Enqueue(entry);
        }

        if (synchronous && sameThread)
        {
            marshaler.RunInvokeQueue();
        }
        else
        {
            marshaler.PostInvokeQueue();

            if (synchronous) entry.AsyncWaitHandle.WaitOne();
        }

        return entry;
    }

    private void PostInvokeQueue()
    {
        lock (_invokeGate)
        {
            if (_invokePosted) return;

            _invokePosted = true;
        }

        Dispatcher.UIThread.Post(RunInvokeQueue);
    }

    /// <summary>Runs every call waiting on this control, in the order they came.</summary>
    private void RunInvokeQueue()
    {
        while (true)
        {
            InvokeEntry entry;

            lock (_invokeGate)
            {
                if (_invokeQueue is not { Count: > 0 } queue)
                {
                    _invokePosted = false;
                    break;
                }

                entry = queue.Dequeue();
            }

            if (!IsHandleCreated)
            {
                entry.Complete(null, new ObjectDisposedException(GetType().Name));
                continue;
            }

            Run(entry);
        }

        // A marshalled call is a message processed: once the queue has emptied, the application is idle again.
        Application.ScheduleIdle();
    }

    private static void Run(InvokeEntry entry)
    {
        object? result = null;
        Exception? failure = null;

        try
        {
            if (entry.Context is { } context) ExecutionContext.Run(context, static state => ((InvokeEntry)state!).Result = ((InvokeEntry)state!).Call(), entry);
            else entry.Result = entry.Call();

            result = entry.Result;
        }
        catch (Exception exception)
        {
            // WinForms hands back the innermost exception, which also unwraps DynamicInvoke's TargetInvocationException.
            failure = exception.GetBaseException();
        }

        entry.Complete(result, failure);

        // A BeginInvoke nobody waits for reports its failure like any other handler in the message loop.
        if (failure is not null && !entry.Synchronous)
        {
            if (!Application.HandlesThreadExceptions) ExceptionDispatchInfo.Throw(failure);

            Application.OnThreadException(failure);
        }
    }

    /// <summary>
    /// The handle is going away: the calls still waiting can never run on it,
    /// so their callers get an ObjectDisposedException instead of waiting forever.
    /// </summary>
    private void FailPendingInvokes()
    {
        InvokeEntry[] pending;

        lock (_invokeGate)
        {
            if (_invokeQueue is not { Count: > 0 } queue) return;

            pending = [.. queue];
            queue.Clear();
        }

        foreach (var entry in pending) entry.Complete(null, new ObjectDisposedException(GetType().Name));
    }

    /// <summary>
    /// Throws when another thread touches the handle while
    /// CheckForIllegalCrossThreadCalls is on, as WinForms does in Handle.
    /// </summary>
    private void VerifyHandleThread()
    {
        if (!CheckForIllegalCrossThreadCalls || !IsHandleCreated) return;
        if (_owningThreadId == Environment.CurrentManagedThreadId) return;

        throw new InvalidOperationException($"Cross-thread operation not valid: Control '{Name}' accessed from a thread other than the thread it was created on.");
    }

    /// <summary>One queued call; it is also the IAsyncResult BeginInvoke returns.</summary>
    private sealed class InvokeEntry(Control caller, Control marshaler, Delegate method, object?[]? args, bool synchronous, ExecutionContext? context) : IAsyncResult
    {
        private readonly Lock _gate = new();
        private ManualResetEvent? _waitHandle;
        private volatile bool _completed;

        public Control Caller { get; } = caller;

        public Control Marshaler { get; } = marshaler;

        public bool Synchronous { get; } = synchronous;

        public ExecutionContext? Context { get; } = context;

        public object? Result { get; set; }

        public object? ReturnValue { get; private set; }

        public Exception? Exception { get; private set; }

        public object? AsyncState => null;

        public bool CompletedSynchronously => false;

        public bool IsCompleted => _completed;

        public WaitHandle AsyncWaitHandle
        {
            get
            {
                lock (_gate)
                {
                    return _waitHandle ??= new ManualResetEvent(_completed);
                }
            }
        }

        /// <summary>
        /// Calls the delegate; the delegate types WinForms recognises are
        /// called directly, with the arguments it gives them, and the rest
        /// through reflection.
        /// </summary>
        public object? Call()
        {
            switch (method)
            {
                case EventHandler handler:
                    var sender = args is { Length: > 0 } ? args[0] : Caller;
                    var eventArgs = args is { Length: > 1 } ? (EventArgs?)args[1] ?? EventArgs.Empty : EventArgs.Empty;
                    handler(sender, eventArgs);
                    return null;

                case MethodInvoker invoker:
                    invoker();
                    return null;

                case Action action:
                    action();
                    return null;

                case WaitCallback callback:
                    callback(args is { Length: > 0 } ? args[0] : null);
                    return null;

                default:
                    return method.DynamicInvoke(args);
            }
        }

        public void Complete(object? result, Exception? exception)
        {
            lock (_gate)
            {
                ReturnValue = result;
                Exception = exception;
                _completed = true;
                _waitHandle?.Set();
            }
        }
    }
}
