// Copied from docs/planning/winforms-phase0/generated/events/LinkLabelLinkClickedEventArgs.cs;
// that file's "using System.Windows.Forms.LinkLabel;" does not compile (Link is
// LinkLabel's nested class, not a namespace), so it is aliased here instead.
using Link = System.Windows.Forms.LinkLabel.Link;

namespace System.Windows.Forms;

public class LinkLabelLinkClickedEventArgs : EventArgs
{
    public LinkLabelLinkClickedEventArgs(Link? link)
    {
        Link = link;
    }

    public LinkLabelLinkClickedEventArgs(Link? link, MouseButtons button)
    {
        Link = link;
        Button = button;
    }

    public MouseButtons Button { get; }

    public Link? Link { get; }
}
