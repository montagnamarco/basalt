using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms.Design;

/// <summary>
/// One page of a ComponentEditorForm: a Panel that loads a component's state
/// into its own controls (LoadComponent), writes it back (SaveComponent),
/// and tells its site when something on it changes (SetDirty).
/// </summary>
/// <remarks>
/// LoadComponent/SaveComponent are abstract: this class only owns the
/// bookkeeping every concrete page needs (which component, which site, the
/// first-activate/loading/dirty state machine) - a real page is a subclass
/// that actually builds controls and reads/writes the component's properties.
/// </remarks>
public abstract class ComponentEditorPage : Panel
{
    private IComponent? _component;
    private IComponentEditorPageSite? _pageSite;
    private bool _firstActivate = true;
    private bool _loadRequired = true;
    private int _loading;
    private bool _commitOnDeactivate = true;
    private Icon? _icon;

    public ComponentEditorPage()
    {
    }

    protected IComponent Component => _component ?? throw new InvalidOperationException("ComponentEditorPage.Component is not set until SetComponent has been called.");

    protected override CreateParams CreateParams => base.CreateParams;

    protected bool FirstActivate
    {
        get => _firstActivate;
        set => _firstActivate = value;
    }

    protected bool LoadRequired
    {
        get => _loadRequired;
        set => _loadRequired = value;
    }

    protected int Loading
    {
        get => _loading;
        set => _loading = value;
    }

    protected IComponentEditorPageSite PageSite => _pageSite ?? throw new InvalidOperationException("ComponentEditorPage.PageSite is not set until SetSite has been called.");

    protected void EnterLoadingMode() => _loading++;

    protected void ExitLoadingMode() => _loading--;

    protected IComponent GetSelectedComponent() => Component;

    protected bool IsFirstActivate() => _firstActivate;

    protected bool IsLoading() => _loading > 0;

    protected abstract void LoadComponent();

    protected virtual void ReloadComponent() => LoadComponent();

    protected abstract void SaveComponent();

    protected virtual void SetDirty() => _pageSite?.SetDirty();

    public virtual void Activate()
    {
        if (_firstActivate || _loadRequired)
        {
            EnterLoadingMode();
            try
            {
                if (_firstActivate) LoadComponent();
                else ReloadComponent();
            }
            finally
            {
                ExitLoadingMode();
            }

            _firstActivate = false;
            _loadRequired = false;
        }

        Visible = true;
    }

    public virtual void ApplyChanges()
    {
        SaveComponent();
        OnApplyComplete();
    }

    public virtual void Deactivate()
    {
        if (_commitOnDeactivate) ApplyChanges();
        Visible = false;
    }

    public virtual Control GetControl() => this;

    public virtual bool IsPageMessage(ref Message msg) => false;

    public virtual void OnApplyComplete()
    {
    }

    public virtual void SetComponent(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        _component = component;
        _loadRequired = true;
    }

    public virtual void SetSite(IComponentEditorPageSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        _pageSite = site;
    }

    public virtual void ShowHelp()
    {
        // No help viewer backs this family's designer (see Help.cs's remarks); a page with help asks its site to show it, which has nothing to show.
    }

    public virtual bool SupportsHelp() => false;

    [DefaultValue(true)]
    public bool CommitOnDeactivate
    {
        get => _commitOnDeactivate;
        set => _commitOnDeactivate = value;
    }

    public Icon? Icon
    {
        get => _icon;
        set => _icon = value;
    }

    public virtual string Title => GetType().Name;

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
}
