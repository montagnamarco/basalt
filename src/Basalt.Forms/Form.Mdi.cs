using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// The multiple-document interface: a container form shows other forms as
/// child windows inside its <see cref="MdiClient"/>, each with its own frame
/// and title bar drawn in the container (see Form.Child.cs).
/// </summary>
public partial class Form
{
    private MdiClient? _mdiClient;
    private Form? _mdiParent;
    private Form? _activeMdiChild;

    /// <summary>Whether this form hosts MDI children, in an MdiClient that fills its client area.</summary>
    [DefaultValue(false)]
    public bool IsMdiContainer
    {
        get => _mdiClient is not null;
        set
        {
            if (value == IsMdiContainer) return;

            if (value)
            {
                if (IsMdiChild) throw new InvalidOperationException("An MDI child form cannot also be an MDI container.");

                _mdiClient = new MdiClient();
                Controls.Add(_mdiClient);

                // Behind docked bars (a menu, a status strip), filling what they leave.
                _mdiClient.SendToBack();
                return;
            }

            if (_mdiClient!.MdiChildren.Length > 0) throw new InvalidOperationException("The form has MDI children: close them before it stops being an MDI container.");

            var client = _mdiClient;
            _mdiClient = null;
            Controls.Remove(client);
            client.Dispose();
        }
    }

    /// <summary>The MDI container this form is a child of; setting it moves the form into the container's MdiClient.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Form? MdiParent
    {
        get => _mdiParent;
        set
        {
            if (value == _mdiParent) return;

            if (value is not null)
            {
                if (!value.IsMdiContainer) throw new ArgumentException("The form specified to be the MdiParent for this form is not an MdiContainer.", nameof(value));
                if (IsMdiContainer) throw new ArgumentException("A form cannot be both an MDI child and an MDI parent.", nameof(value));
            }

            // A shown form changes from window to child (or back) the way Windows recreates it:
            // hidden, its handle (and native window, if it had one) destroyed, then shown again.
            var wasVisible = Visible;
            if (wasVisible) Visible = false;
            if (IsHandleCreated) DestroyHandle();

            var previous = _mdiParent;
            _mdiParent = null;
            previous?.MdiChildRemoved(this);

            if (value is null)
            {
                Parent = null;
                TopLevel = true;
            }
            else
            {
                TopLevel = false;
                _mdiParent = value;
                value._mdiClient!.Controls.Add(this);
            }

            if (wasVisible) Visible = true;
        }
    }

    /// <summary>Whether this form is the child of an MDI container.</summary>
    [Browsable(false)]
    public bool IsMdiChild => _mdiParent is not null;

    /// <summary>This container's MDI children, in the order they were added; empty for any other form.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Form[] MdiChildren => _mdiClient?.MdiChildren ?? [];

    /// <summary>The MDI child drawn active and holding the keyboard, or null.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Form? ActiveMdiChild => _activeMdiChild is { IsDisposed: false, Visible: true } child ? child : null;

    /// <summary>Whether minimized children line up along the bottom of the container (true) or the top.</summary>
    [DefaultValue(true)]
    public bool MdiChildrenMinimizedAnchorBottom { get; set; } = true;

    /// <summary>Cascades or tiles the MDI children, or lines up the minimized ones.</summary>
    public void LayoutMdi(MdiLayout value) => _mdiClient?.LayoutMdi(value);

    /// <summary>Makes the child active: brought to the front, drawn active, given the keyboard.</summary>
    protected void ActivateMdiChild(Form? form)
    {
        if (form is null)
        {
            SetActiveMdiChildCore(null);
            return;
        }

        if (form.MdiParent != this) return;

        form.ActivateAsChild();
    }

    public event EventHandler? MdiChildActivate
    {
        add => Events.AddHandler(s_mdiChildActivateEvent, value);
        remove => Events.RemoveHandler(s_mdiChildActivateEvent, value);
    }

    private static readonly object s_mdiChildActivateEvent = new();

    protected virtual void OnMdiChildActivate(EventArgs e) => ((EventHandler?)Events[s_mdiChildActivateEvent])?.Invoke(this, e);

    /// <summary>Records the new active child, redraws the old and new title bars, and raises MdiChildActivate once.</summary>
    internal void SetActiveMdiChildCore(Form? child)
    {
        if (child == _activeMdiChild) return;

        var previous = _activeMdiChild;
        _activeMdiChild = child;

        previous?.InvalidateFrame();
        child?.InvalidateFrame();

        // The children have no window of their own to tell them: the container raises their
        // Deactivate and Activated, then its own MdiChildActivate, as Windows' MDI client does.
        if (previous is { IsDisposed: false }) previous.OnDeactivate(EventArgs.Empty);
        child?.OnActivated(EventArgs.Empty);

        OnMdiChildActivate(EventArgs.Empty);
    }

    /// <summary>A child left the container (closed, disposed or given another parent): the next one becomes active.</summary>
    internal void MdiChildRemoved(Form child)
    {
        if (_activeMdiChild != child) return;

        var next = MdiChildren.LastOrDefault(form => form != child && form.Visible);
        if (next is not null) next.ActivateAsChild();
        else SetActiveMdiChildCore(null);
    }

    /// <summary>
    /// Ctrl+F4 closes the active child, Ctrl+Tab and Ctrl+F6 move to the next child and
    /// Ctrl+Shift+Tab and Ctrl+Shift+F6 to the previous one, as Windows does in an MDI frame.
    /// </summary>
    private bool ProcessMdiKey(Keys keyData)
    {
        if (_mdiClient is null || ActiveMdiChild is not { } active) return false;

        switch (keyData)
        {
            case Keys.Control | Keys.F4:
                if (active.ControlBox) active.Close();
                return true;

            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.F6:
                _mdiClient.NextChild(active, forward: true)?.ActivateAsChild();
                return true;

            case Keys.Control | Keys.Shift | Keys.Tab:
            case Keys.Control | Keys.Shift | Keys.F6:
                _mdiClient.NextChild(active, forward: false)?.ActivateAsChild();
                return true;

            default:
                return false;
        }
    }
}
