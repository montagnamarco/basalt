namespace System.Windows.Forms;

/// <summary>Sees every message before the control it is for does; true consumes it.</summary>
public interface IMessageFilter
{
    bool PreFilterMessage(ref Message m);
}
