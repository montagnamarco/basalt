using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace System.Drawing;

/// <summary>The quality properties, the page unit/scale, and the Save/Restore and BeginContainer/EndContainer state stack.</summary>
public sealed partial class Graphics
{
    private GraphicsUnit _pageUnit = GraphicsUnit.Pixel;
    private float _pageScale = 1f;

    // One shared stack for both Save/Restore and BeginContainer/EndContainer:
    // GDI+ treats them as the same kind of state frame, and restoring either
    // one discards every frame pushed after it.
    private readonly List<SavedState> _stateStack = [];
    private long _nextStateId = 1;

    public CompositingMode CompositingMode { get; set; } = CompositingMode.SourceOver;

    public CompositingQuality CompositingQuality { get; set; } = CompositingQuality.Default;

    public InterpolationMode InterpolationMode { get; set; } = InterpolationMode.Default;

    public TextRenderingHint TextRenderingHint { get; set; } = TextRenderingHint.SystemDefault;

    public int TextContrast { get; set; } = 4;

    public Point RenderingOrigin { get; set; } = Point.Empty;

    /// <summary>There is no real device to ask: this assembly always reports the one DPI it draws at.</summary>
    public float DpiX => MatrixInterop.Dpi;

    public float DpiY => MatrixInterop.Dpi;

    public GraphicsUnit PageUnit
    {
        get => _pageUnit;
        set => _pageUnit = value;
    }

    public float PageScale
    {
        get => _pageScale;
        set => _pageScale = value;
    }

    public GraphicsState Save() => new(PushState(null));

    public void Restore(GraphicsState gstate)
    {
        ArgumentNullException.ThrowIfNull(gstate);

        Restore(gstate.Id);
    }

    public GraphicsContainer BeginContainer() => new(PushState(null));

    public GraphicsContainer BeginContainer(Rectangle dstrect, Rectangle srcrect, GraphicsUnit unit) =>
        BeginContainer(new RectangleF(dstrect.X, dstrect.Y, dstrect.Width, dstrect.Height), new RectangleF(srcrect.X, srcrect.Y, srcrect.Width, srcrect.Height), unit);

    public GraphicsContainer BeginContainer(RectangleF dstrect, RectangleF srcrect, GraphicsUnit unit) =>
        new(PushState(ContainerMapping(dstrect, srcrect, unit)));

    public void EndContainer(GraphicsContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        Restore(container.Id);
    }

    /// <summary>What Save/BeginContainer capture and Restore/EndContainer put back, one snapshot per frame.</summary>
    private sealed class SavedState(long id, int canvasSaveCount, QualitySnapshot quality, System.Numerics.Matrix3x2 worldTransform, SkiaSharp.SKPath? clip)
    {
        public long Id { get; } = id;
        public int CanvasSaveCount { get; } = canvasSaveCount;
        public QualitySnapshot Quality { get; } = quality;
        public System.Numerics.Matrix3x2 WorldTransform { get; } = worldTransform;
        public SkiaSharp.SKPath? Clip { get; } = clip;
    }

    private readonly record struct QualitySnapshot(
        SmoothingMode SmoothingMode,
        PixelOffsetMode PixelOffsetMode,
        CompositingMode CompositingMode,
        CompositingQuality CompositingQuality,
        InterpolationMode InterpolationMode,
        TextRenderingHint TextRenderingHint,
        int TextContrast,
        GraphicsUnit PageUnit,
        float PageScale,
        Point RenderingOrigin);

    private QualitySnapshot CaptureQuality() => new(
        SmoothingMode, PixelOffsetMode, CompositingMode, CompositingQuality, InterpolationMode,
        TextRenderingHint, TextContrast, PageUnit, PageScale, RenderingOrigin);

    private void ApplyQuality(QualitySnapshot quality)
    {
        SmoothingMode = quality.SmoothingMode;
        PixelOffsetMode = quality.PixelOffsetMode;
        CompositingMode = quality.CompositingMode;
        CompositingQuality = quality.CompositingQuality;
        InterpolationMode = quality.InterpolationMode;
        TextRenderingHint = quality.TextRenderingHint;
        TextContrast = quality.TextContrast;
        PageUnit = quality.PageUnit;
        PageScale = quality.PageScale;
        RenderingOrigin = quality.RenderingOrigin;
    }

    /// <summary>
    /// Pushes a state frame, optionally concatenating a mapping matrix on top
    /// of the world transform for a BeginContainer(dst, src, unit) call, and
    /// returns the token both Save and BeginContainer hand out.
    /// </summary>
    private long PushState(System.Numerics.Matrix3x2? mapping)
    {
        var canvasSaveCount = _canvas.Save();
        var state = new SavedState(_nextStateId++, canvasSaveCount, CaptureQuality(), _worldTransform, CopyClip());
        _stateStack.Add(state);

        if (mapping is { } m) _worldTransform = MatrixInterop.Combine(_worldTransform, m, MatrixOrder.Prepend);

        return state.Id;
    }

    /// <summary>
    /// The matrix that remaps coordinates given in srcrect's numbers (in the
    /// stated unit) so that they land in dstrect (in the Graphics's current
    /// coordinate system): a scale and a translation built from the ratio of
    /// the two rectangles. The GraphicsUnit cancels out of that ratio and only
    /// matters if dst and src are measured in different absolute sizes, which
    /// this constructor does not allow; this could not be checked against a
    /// real GDI+ container without a Windows machine.
    /// </summary>
    private static System.Numerics.Matrix3x2 ContainerMapping(RectangleF dstrect, RectangleF srcrect, GraphicsUnit unit)
    {
        _ = unit;

        var scaleX = srcrect.Width == 0 ? 1f : dstrect.Width / srcrect.Width;
        var scaleY = srcrect.Height == 0 ? 1f : dstrect.Height / srcrect.Height;

        var translate = System.Numerics.Matrix3x2.CreateTranslation(dstrect.X - srcrect.X * scaleX, dstrect.Y - srcrect.Y * scaleY);
        return MatrixInterop.Combine(System.Numerics.Matrix3x2.CreateScale(scaleX, scaleY), translate, MatrixOrder.Append);
    }

    /// <summary>Drops the saved states from this position on, with the clips they hold.</summary>
    private void DiscardStatesFrom(int index)
    {
        for (var i = index; i < _stateStack.Count; i++)
        {
            _stateStack[i].Clip?.Dispose();
        }

        _stateStack.RemoveRange(index, _stateStack.Count - index);
    }

    /// <summary>
    /// Restores the frame with this id, and discards every frame saved after
    /// it: GDI+'s documented behaviour for both Restore and EndContainer.
    /// Restoring an id that is not on the stack (already discarded, or never
    /// this Graphics's) does nothing.
    /// </summary>
    private void Restore(long id)
    {
        var index = _stateStack.FindIndex(s => s.Id == id);
        if (index < 0) return;

        var target = _stateStack[index];

        // The clip of the restored state becomes the live one; those of the
        // states saved after it are dropped with them.
        DiscardStatesFrom(index + 1);
        _stateStack.RemoveAt(index);

        _canvas.RestoreToCount(target.CanvasSaveCount);
        _worldTransform = target.WorldTransform;
        ReplaceClip(target.Clip);
        ApplyQuality(target.Quality);
    }
}
