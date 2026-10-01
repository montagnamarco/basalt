namespace System.Windows.Forms;

/// <summary>
/// The application-wide default for every <see cref="ToolStrip"/> that has
/// not been given a renderer of its own: a mode (System or Professional) or
/// a custom <see cref="ToolStripRenderer"/>. Merging several strips into one
/// is not implemented in this round.
/// </summary>
public static class ToolStripManager
{
    private static ToolStripRenderer? _renderer;
    private static ToolStripManagerRenderMode _renderMode = ToolStripManagerRenderMode.System;

    private static readonly ToolStripSystemRenderer SystemRendererInstance = new();
    private static readonly ToolStripProfessionalRenderer ProfessionalRendererInstance = new();

    public static ToolStripManagerRenderMode RenderMode
    {
        get => _renderMode;
        set
        {
            if (value == ToolStripManagerRenderMode.Custom && _renderer is null)
            {
                throw new NotSupportedException("RenderMode.Custom is applied automatically when Renderer is set; set Renderer instead.");
            }

            if (value == _renderMode) return;

            _renderMode = value;
            OnRendererChanged();
        }
    }

    /// <summary>The default renderer: the one explicitly set, else a stock one for the current mode.</summary>
    public static ToolStripRenderer Renderer
    {
        get => _renderMode switch
        {
            ToolStripManagerRenderMode.Professional => ProfessionalRendererInstance,
            ToolStripManagerRenderMode.Custom => _renderer ?? SystemRendererInstance,
            _ => SystemRendererInstance,
        };
        set
        {
            _renderer = value;
            _renderMode = ToolStripManagerRenderMode.Custom;
            OnRendererChanged();
        }
    }

    /// <summary>Whether a native theme is available to render with; this project paints the classic theme always.</summary>
    public static bool VisualStylesEnabled { get; set; }

    public static event EventHandler? RendererChanged;

    // Strips that follow the manager's renderer repaint when it changes. Held
    // weakly so a strip nobody references can still be collected: a strong
    // static list (or a static event subscription) would keep every strip
    // ever created alive for the life of the process.
    private static readonly List<WeakReference<ToolStrip>> s_strips = [];
    private static readonly Lock s_stripsLock = new();

    internal static void Track(ToolStrip strip)
    {
        lock (s_stripsLock)
        {
            s_strips.RemoveAll(reference => !reference.TryGetTarget(out _));
            s_strips.Add(new WeakReference<ToolStrip>(strip));
        }
    }

    private static void OnRendererChanged()
    {
        List<ToolStrip> live = [];
        lock (s_stripsLock)
        {
            foreach (var reference in s_strips)
            {
                if (reference.TryGetTarget(out var strip)) live.Add(strip);
            }
        }

        foreach (var strip in live) strip.OnManagerRendererChanged();

        RendererChanged?.Invoke(null, EventArgs.Empty);
    }
}
