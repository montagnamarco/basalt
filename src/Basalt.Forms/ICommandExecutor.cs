namespace System.Windows.Forms;

/// <summary>Something a command source (a menu item, a button) can run when activated.</summary>
public interface ICommandExecutor
{
    void Execute();
}
