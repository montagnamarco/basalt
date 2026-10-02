using System.Drawing;
using System.Windows.Forms;
using System.Windows.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>ButtonBase.Command: clicking runs the bound ICommand, and CanExecute drives Enabled.</summary>
public class ButtonCommandTests
{
    private sealed class TestCommand : ICommand
    {
        private bool _canExecute = true;
        public object? LastParameter;
        public int ExecuteCount;

        public bool CanExecute(object? parameter) => _canExecute;

        public void Execute(object? parameter)
        {
            ExecuteCount++;
            LastParameter = parameter;
        }

        public event EventHandler? CanExecuteChanged;

        public void SetCanExecute(bool value)
        {
            _canExecute = value;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void Clicking_the_button_executes_the_bound_command_with_its_parameter()
    {
        var command = new TestCommand();
        var button = new Button { Bounds = new Rectangle(10, 10, 80, 30), Command = command, CommandParameter = "payload" };
        var form = ShowWith(button);

        button.PerformClick();

        Assert.Equal(1, command.ExecuteCount);
        Assert.Equal("payload", command.LastParameter);

        form.Close();
    }

    [AvaloniaFact]
    public void A_command_that_cannot_execute_disables_the_button_and_raises_CommandCanExecuteChanged()
    {
        var command = new TestCommand();
        var button = new Button { Bounds = new Rectangle(10, 10, 80, 30), Command = command };
        var raised = 0;
        button.CommandCanExecuteChanged += (_, _) => raised++;

        Assert.True(button.Enabled);

        command.SetCanExecute(false);

        Assert.False(button.Enabled);
        Assert.Equal(1, raised);

        command.SetCanExecute(true);
        Assert.True(button.Enabled);
    }
}
