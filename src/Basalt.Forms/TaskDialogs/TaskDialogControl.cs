namespace System.Windows.Forms;

/// <summary>
/// Base of every piece that can be attached to a <see cref="TaskDialogPage"/>:
/// buttons, the radio buttons, the verification check box, the expander, the
/// footnote and the progress bar. Knows which page owns it and, once that page
/// is the one a <see cref="TaskDialog"/> is currently showing, which page it
/// is bound to.
/// </summary>
public abstract class TaskDialogControl
{
    /// <summary>Only Basalt.Forms derives from this (as Microsoft's own assembly does): the constructor is internal.</summary>
    internal TaskDialogControl()
    {
    }

    /// <summary>
    /// The page this control was added to, whether or not that page is
    /// currently shown. Set by the owning page or collection; never by user
    /// code, since nothing here is virtual for that purpose.
    /// </summary>
    internal TaskDialogPage? OwnerPage { get; set; }

    /// <summary>
    /// The page this control belongs to while that page is the one a dialog
    /// has on screen; null before the dialog shows it and after it stops
    /// showing it (replaced by Navigate, or the dialog closed).
    /// </summary>
    public TaskDialogPage? BoundPage => OwnerPage?.BoundDialog is not null ? OwnerPage : null;

    public object? Tag { get; set; }

    /// <summary>Asks the bound page's form to redraw or relayout this control, if one is showing it.</summary>
    internal void NotifyBoundFormOfChange() => OwnerPage?.NotifyBoundFormOfChange();
}
