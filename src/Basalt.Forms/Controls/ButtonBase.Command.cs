using System.ComponentModel;
using System.Windows.Input;

namespace System.Windows.Forms;

/// <summary>
/// .NET's WinForms commanding: a button can bind to an <see cref="ICommand"/>
/// instead of wiring Click by hand. Clicking calls RequestCommandExecute,
/// which runs the command when it says it can; the command's own
/// CanExecuteChanged keeps the button's Enabled state (and the mirrored
/// CommandCanExecuteChanged event) in step with it.
/// </summary>
public abstract partial class ButtonBase
{
    private ICommand? _command;
    private object? _commandParameter;

    [Category("Behavior")]
    public ICommand? Command
    {
        get => _command;
        set
        {
            if (_command == value) return;

            if (_command is not null) _command.CanExecuteChanged -= CommandCanExecuteChangedHandler;

            _command = value;

            if (_command is not null) _command.CanExecuteChanged += CommandCanExecuteChangedHandler;

            UpdateEnabledFromCommand();
            OnCommandChanged(EventArgs.Empty);
        }
    }

    [Category("Behavior")]
    public object? CommandParameter
    {
        get => _commandParameter;
        set
        {
            if (Equals(_commandParameter, value)) return;

            _commandParameter = value;
            UpdateEnabledFromCommand();
            OnCommandParameterChanged(EventArgs.Empty);
        }
    }

    [Category("Property Changed")]
    public event EventHandler? CommandChanged
    {
        add => Events.AddHandler(s_commandChangedEvent, value);
        remove => Events.RemoveHandler(s_commandChangedEvent, value);
    }

    [Category("Property Changed")]
    public event EventHandler? CommandCanExecuteChanged
    {
        add => Events.AddHandler(s_commandCanExecuteChangedEvent, value);
        remove => Events.RemoveHandler(s_commandCanExecuteChangedEvent, value);
    }

    [Category("Property Changed")]
    public event EventHandler? CommandParameterChanged
    {
        add => Events.AddHandler(s_commandParameterChangedEvent, value);
        remove => Events.RemoveHandler(s_commandParameterChangedEvent, value);
    }

    private static readonly object s_commandChangedEvent = new();
    private static readonly object s_commandCanExecuteChangedEvent = new();
    private static readonly object s_commandParameterChangedEvent = new();

    protected virtual void OnCommandChanged(EventArgs e) => ((EventHandler?)Events[s_commandChangedEvent])?.Invoke(this, e);

    protected virtual void OnCommandCanExecuteChanged(EventArgs e) => ((EventHandler?)Events[s_commandCanExecuteChangedEvent])?.Invoke(this, e);

    protected virtual void OnCommandParameterChanged(EventArgs e) => ((EventHandler?)Events[s_commandParameterChangedEvent])?.Invoke(this, e);

    /// <summary>A click asks the bound command to run, same as WinForms: nothing happens if there is none or it refuses.</summary>
    protected virtual void OnRequestCommandExecute(EventArgs e)
    {
        if (_command is { } command && command.CanExecute(_commandParameter)) command.Execute(_commandParameter);
    }

    private void CommandCanExecuteChangedHandler(object? sender, EventArgs e)
    {
        UpdateEnabledFromCommand();
        OnCommandCanExecuteChanged(EventArgs.Empty);
    }

    /// <summary>A command that currently cannot execute disables the button, mirroring WPF's command binding.</summary>
    private void UpdateEnabledFromCommand()
    {
        if (_command is { } command) Enabled = command.CanExecute(_commandParameter);
    }
}
