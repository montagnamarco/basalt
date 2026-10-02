using System.ComponentModel;
using System.Windows.Input;

namespace System.Windows.Forms;

/// <summary>
/// The MVVM-style command an item can be bound to instead of (or alongside)
/// a Click handler: setting <see cref="Command"/> drives <see cref="ToolStripItem.Enabled"/>
/// from <see cref="ICommand.CanExecute"/> and a click executes it, the same
/// way Microsoft wired Command onto buttons and menu items.
/// </summary>
public abstract partial class ToolStripItem
{
    private ICommand? _command;
    private object? _commandParameter;

    [DefaultValue(null)]
    [Bindable(true)]
    public ICommand? Command
    {
        get => _command;
        set
        {
            if (ReferenceEquals(value, _command)) return;

            if (_command is not null) _command.CanExecuteChanged -= HandleCommandCanExecuteChanged;
            _command = value;
            if (_command is not null) _command.CanExecuteChanged += HandleCommandCanExecuteChanged;

            OnCommandChanged(EventArgs.Empty);
            UpdateEnabledFromCommand();
        }
    }

    [DefaultValue(null)]
    [Bindable(true)]
    public object? CommandParameter
    {
        get => _commandParameter;
        set
        {
            if (Equals(value, _commandParameter)) return;

            _commandParameter = value;
            OnCommandParameterChanged(EventArgs.Empty);
            UpdateEnabledFromCommand();
        }
    }

    public event EventHandler? CommandCanExecuteChanged
    {
        add => Events.AddHandler(s_commandCanExecuteChangedEvent, value);
        remove => Events.RemoveHandler(s_commandCanExecuteChangedEvent, value);
    }

    public event EventHandler? CommandChanged
    {
        add => Events.AddHandler(s_commandChangedEvent, value);
        remove => Events.RemoveHandler(s_commandChangedEvent, value);
    }

    public event EventHandler? CommandParameterChanged
    {
        add => Events.AddHandler(s_commandParameterChangedEvent, value);
        remove => Events.RemoveHandler(s_commandParameterChangedEvent, value);
    }

    private static readonly object s_commandCanExecuteChangedEvent = new();
    private static readonly object s_commandChangedEvent = new();
    private static readonly object s_commandParameterChangedEvent = new();

    protected virtual void OnCommandCanExecuteChanged(EventArgs e) => ((EventHandler?)Events[s_commandCanExecuteChangedEvent])?.Invoke(this, e);

    protected virtual void OnCommandChanged(EventArgs e) => ((EventHandler?)Events[s_commandChangedEvent])?.Invoke(this, e);

    protected virtual void OnCommandParameterChanged(EventArgs e) => ((EventHandler?)Events[s_commandParameterChangedEvent])?.Invoke(this, e);

    /// <summary>
    /// What a click asks the bound command to do: the seam a subclass (a
    /// split button's arrow half, say) overrides to decide whether its own
    /// click should reach the command at all.
    /// </summary>
    protected virtual void OnRequestCommandExecute(EventArgs e)
    {
        if (_command is { } command && command.CanExecute(_commandParameter)) command.Execute(_commandParameter);
    }

    private void HandleCommandCanExecuteChanged(object? sender, EventArgs e)
    {
        UpdateEnabledFromCommand();
        OnCommandCanExecuteChanged(EventArgs.Empty);
    }

    private void UpdateEnabledFromCommand()
    {
        if (_command is { } command) Enabled = command.CanExecute(_commandParameter);
    }
}
