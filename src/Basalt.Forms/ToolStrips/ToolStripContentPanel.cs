using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// The middle of a <see cref="ToolStripContainer"/>: an ordinary <see cref="Panel"/>
/// (the form's real content goes here) that additionally knows which
/// <see cref="ToolStripRenderer"/> paints its background and raises
/// <see cref="Load"/> once, the first time its handle is created.
/// </summary>
[ToolboxItem(false)]
public partial class ToolStripContentPanel : Panel
{
    private ToolStripRenderer? _renderer;
    private ToolStripRenderMode _renderMode = ToolStripRenderMode.ManagerRenderMode;
    private bool _loadRaised;

    private static readonly ToolStripSystemRenderer SystemRendererInstance = new();
    private static readonly ToolStripProfessionalRenderer ProfessionalRendererInstance = new();

    public ToolStripRenderMode RenderMode
    {
        get => _renderMode;
        set
        {
            if (value == ToolStripRenderMode.Custom) throw new NotSupportedException("RenderMode.Custom is applied automatically when Renderer is set; set Renderer instead.");
            if (value == _renderMode) return;

            _renderMode = value;
            OnRendererChanged(EventArgs.Empty);
            Invalidate(true);
        }
    }

    [Browsable(false)]
    public ToolStripRenderer Renderer
    {
        get => _renderMode switch
        {
            ToolStripRenderMode.System => SystemRendererInstance,
            ToolStripRenderMode.Professional => ProfessionalRendererInstance,
            ToolStripRenderMode.Custom => _renderer ?? SystemRendererInstance,
            _ => ToolStripManager.Renderer,
        };
        set
        {
            _renderer = value;
            _renderMode = ToolStripRenderMode.Custom;
            OnRendererChanged(EventArgs.Empty);
            Invalidate(true);
        }
    }

    public event EventHandler? Load;

    public event EventHandler? RendererChanged;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_loadRaised) return;

        _loadRaised = true;
        OnLoad(EventArgs.Empty);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnLoad(EventArgs e) => Load?.Invoke(this, e);

    protected virtual void OnRendererChanged(EventArgs e) => RendererChanged?.Invoke(this, e);
}
