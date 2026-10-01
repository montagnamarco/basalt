using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>What a Binding reports once a push or a pull finishes: whether it worked, and why not when it did not.</summary>
public class BindingCompleteEventArgs : CancelEventArgs
{
    public BindingCompleteEventArgs(Binding? binding, BindingCompleteState state, BindingCompleteContext context)
        : this(binding, state, context, string.Empty, null, false)
    {
    }

    public BindingCompleteEventArgs(Binding? binding, BindingCompleteState state, BindingCompleteContext context, string? errorText)
        : this(binding, state, context, errorText, null, false)
    {
    }

    public BindingCompleteEventArgs(Binding? binding, BindingCompleteState state, BindingCompleteContext context, string? errorText, Exception? exception)
        : this(binding, state, context, errorText, exception, state != BindingCompleteState.Success)
    {
    }

    public BindingCompleteEventArgs(Binding? binding, BindingCompleteState state, BindingCompleteContext context, string? errorText, Exception? exception, bool cancel)
        : base(cancel)
    {
        Binding = binding;
        BindingCompleteState = state;
        BindingCompleteContext = context;
        ErrorText = errorText ?? string.Empty;
        Exception = exception;
    }

    public Binding? Binding { get; }

    public BindingCompleteContext BindingCompleteContext { get; }

    public BindingCompleteState BindingCompleteState { get; }

    public string ErrorText { get; }

    public Exception? Exception { get; }
}
