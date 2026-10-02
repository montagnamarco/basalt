using System.ComponentModel;

namespace System.Windows.Forms.Design;

/// <summary>
/// The classic multi-page component editor: a list of page titles down the
/// left (TreeView, as Microsoft's own selector is), the selected page's
/// controls filling the rest, and OK/Cancel/Apply/Help across the bottom.
/// </summary>
/// <remarks>
/// Switching pages deactivates the one being left - which, when its
/// CommitOnDeactivate is true (the default), calls its ApplyChanges - the
/// same "earlier tabs are already committed" behaviour the real multi-page
/// property dialogs have: Cancel only skips applying whichever page is
/// showing when it is pressed, not ones already left. ShowForm reports
/// whether anything was ever applied, through IsDirty, for
/// WindowsFormsComponentEditor.EditComponent to turn into its own return
/// value.
/// </remarks>
public class ComponentEditorForm : Form
{
    private readonly object _component;
    private readonly ComponentEditorPage[] _pages;
    private readonly TreeView _selector;
    private readonly Panel _pageHost;
    private readonly Button _okButton;
    private readonly Button _cancelButton;
    private readonly Button _applyButton;
    private readonly Button _helpButton;

    private int _selectedIndex = -1;
    private bool _dirty;

    public ComponentEditorForm(object component, Type[] pages)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Length == 0) throw new ArgumentException("A component editor needs at least one page.", nameof(pages));

        _component = component;

        _pages = new ComponentEditorPage[pages.Length];
        for (var i = 0; i < pages.Length; i++)
        {
            var page = (ComponentEditorPage)Activator.CreateInstance(pages[i])!;
            page.SetSite(new PageSite(this));
            if (component is IComponent asComponent) page.SetComponent(asComponent);
            _pages[i] = page;
        }

        Text = "Properties";
        Size = new Drawing.Size(500, 400);
        StartPosition = FormStartPosition.CenterParent;

        _selector = new TreeView { Dock = DockStyle.Left, Width = 140, HideSelection = false };
        foreach (var page in _pages) _selector.Nodes.Add(new TreeNode(page.Title));
        _selector.AfterSelect += (sender, e) => OnSelChangeSelector(sender, e);

        // Every page's control becomes a real child of _pageHost right away
        // (hidden until selected, not added/removed as the selection changes):
        // that is what lets the Form's own Dispose - Control.Dispose always
        // disposes its Controls collection - dispose every page, including
        // ones never shown, without ComponentEditorForm needing a Dispose
        // override of its own (Microsoft's does not have one either).
        _pageHost = new Panel { Dock = DockStyle.Fill };
        foreach (var page in _pages)
        {
            var control = page.GetControl();
            control.Dock = DockStyle.Fill;
            control.Visible = false;
            _pageHost.Controls.Add(control);
        }

        _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK };
        _cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        _applyButton = new Button { Text = "Apply" };
        _helpButton = new Button { Text = "Help", Visible = Array.Exists(_pages, p => p.SupportsHelp()) };

        _okButton.Click += (_, _) => ApplyCurrentPage();
        _applyButton.Click += (_, _) => ApplyCurrentPage();
        _helpButton.Click += (_, _) => RaiseHelpRequested(new HelpEventArgs(MousePosition));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 36,
            Controls = { _cancelButton, _okButton, _applyButton, _helpButton },
        };

        Controls.Add(_pageHost);
        Controls.Add(buttons);
        Controls.Add(_selector);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        // No page is activated here: ShowForm(owner, page) selects the requested
        // page itself, the first time _selectedIndex is still -1, so nothing is
        // ever deactivated (and, with CommitOnDeactivate, silently applied)
        // before the dialog has even appeared. Activating page 0 here and then
        // switching to the requested page in ShowForm did exactly that.
    }

    /// <summary>Whether any page's ApplyChanges has actually run, through a page switch or the OK/Apply buttons - what WindowsFormsComponentEditor.EditComponent reports back as its own return value.</summary>
    internal bool IsDirty => _dirty;

    /// <summary>Test-only: every page this form constructed, whether or not it was ever shown.</summary>
    internal ComponentEditorPage[] Pages => _pages;

    private void ApplyCurrentPage()
    {
        if (_selectedIndex < 0) return;

        _pages[_selectedIndex].ApplyChanges();
        _dirty = true;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _selector.Focus();
    }

    protected override void OnHelpRequested(HelpEventArgs hevent)
    {
        if (_selectedIndex >= 0 && _pages[_selectedIndex].SupportsHelp())
        {
            _pages[_selectedIndex].ShowHelp();
            hevent.Handled = true;
            return;
        }

        base.OnHelpRequested(hevent);
    }

    protected virtual void OnSelChangeSelector(object? sender, TreeViewEventArgs e)
    {
        var newIndex = e.Node is null ? -1 : _selector.Nodes.IndexOf(e.Node);
        if (newIndex == _selectedIndex || newIndex < 0) return;

        if (_selectedIndex >= 0)
        {
            var leavingPage = _pages[_selectedIndex];
            leavingPage.Deactivate();
            if (leavingPage.CommitOnDeactivate) _dirty = true;
            leavingPage.GetControl().Visible = false;
        }

        _selectedIndex = newIndex;
        var page = _pages[_selectedIndex];

        page.GetControl().Visible = true;
        page.Activate();
    }

    public override bool PreProcessMessage(ref Message msg) =>
        (_selectedIndex >= 0 && _pages[_selectedIndex].IsPageMessage(ref msg)) || base.PreProcessMessage(ref msg);

    public virtual DialogResult ShowForm() => ShowForm((IWin32Window?)null, 0);

    public virtual DialogResult ShowForm(int page) => ShowForm((IWin32Window?)null, page);

    public virtual DialogResult ShowForm(IWin32Window? owner) => ShowForm(owner, 0);

    public virtual DialogResult ShowForm(IWin32Window? owner, int page)
    {
        if (page >= 0 && page < _selector.Nodes.Count) _selector.SelectedNode = _selector.Nodes[page];

        return owner is null ? ShowDialog() : ShowDialog(owner);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? AutoSizeChanged
    {
        add => base.AutoSizeChanged += value;
        remove => base.AutoSizeChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set => base.AutoSize = value;
    }

    /// <summary>What every page's PageSite resolves to: the one host panel holding whichever page is currently docked into it, and this form's dirty flag.</summary>
    private sealed class PageSite(ComponentEditorForm form) : IComponentEditorPageSite
    {
        public Control GetControl() => form._pageHost;

        public void SetDirty() => form._dirty = true;
    }
}
