using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>Help requests, which go up to the parent until a handler handles them.</summary>
public partial class Control
{
    [Category("Behavior")]
    public event HelpEventHandler? HelpRequested
    {
        add => Events.AddHandler(s_helpRequestedEvent, value);
        remove => Events.RemoveHandler(s_helpRequestedEvent, value);
    }

    private static readonly object s_helpRequestedEvent = new();

    protected virtual void OnHelpRequested(HelpEventArgs hevent)
    {
        ArgumentNullException.ThrowIfNull(hevent);

        if (Events[s_helpRequestedEvent] is HelpEventHandler handler)
        {
            handler(this, hevent);

            // WinForms counts a handler as handling, unless it says otherwise.
            hevent.Handled = true;
        }

        if (!hevent.Handled) _parent?.OnHelpRequested(hevent);
    }

    internal void RaiseHelpRequested(HelpEventArgs arguments) => OnHelpRequested(arguments);
}
