using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// The sunken, application-workspace-coloured area of an MDI container in
/// which its child forms live. A container form creates one when
/// IsMdiContainer is set; it fills what the container's docked bars leave.
/// </summary>
[ToolboxItem(false)]
[DesignTimeVisible(false)]
public sealed class MdiClient : Control
{
    /// <summary>The children in the order they were added: what MdiChildren reports, whatever the z-order.</summary>
    private readonly List<Form> _children = [];

    public MdiClient()
    {
        Dock = DockStyle.Fill;
        BackColor = SystemColors.AppWorkspace;
        SetStyle(ControlStyles.Selectable, false);
    }

    /// <summary>The sunken edge Windows gives the MDI client window.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WindowStyles.ExClientEdge;
            return parameters;
        }
    }

    [Browsable(false)]
    public Form[] MdiChildren => _children.Where(child => !child.IsDisposed).ToArray();

    protected override Control.ControlCollection CreateControlsInstance() => new ControlCollection(this);

    /// <summary>A maximized child keeps filling the client area as the container is resized.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        foreach (var child in _children)
        {
            if (child.WindowState == FormWindowState.Maximized) child.Bounds = child.MaximizedBounds(this);
        }
    }

    /// <summary>The child after (or before) this one, in the order they were added, wrapping round.</summary>
    internal Form? NextChild(Form current, bool forward)
    {
        var candidates = _children.Where(child => child.Visible && !child.IsDisposed).ToList();
        if (candidates.Count < 2) return null;

        var index = candidates.IndexOf(current);
        var step = forward ? 1 : -1;

        return candidates[((index + step) % candidates.Count + candidates.Count) % candidates.Count];
    }

    public void LayoutMdi(MdiLayout value)
    {
        if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(MdiLayout));

        MdiLayoutEngine.Arrange(this, value, _children.Where(child => child.Visible && !child.IsDisposed).ToList());
    }

    /// <summary>The controls of an MdiClient: MDI child forms only.</summary>
    public new class ControlCollection : Control.ControlCollection
    {
        private readonly MdiClient _owner;

        public ControlCollection(MdiClient owner)
            : base(owner)
        {
            _owner = owner;
        }

        public override void Add(Control? value)
        {
            if (value is null) return;
            if (value is not Form { IsMdiChild: true } child) throw new ArgumentException("Only MDI child forms can be added to an MdiClient: set the form's MdiParent instead.", nameof(value));

            base.Add(child);
            if (!_owner._children.Contains(child)) _owner._children.Add(child);
        }

        public override void Remove(Control? value)
        {
            base.Remove(value);

            if (value is Form child) _owner._children.Remove(child);
        }
    }
}
