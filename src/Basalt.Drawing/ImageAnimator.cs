using System.Drawing.Imaging;
using System.Linq;
using System.Threading;

namespace System.Drawing;

/// <summary>
/// Plays a multi-frame image's frames back, one at a time, on a background
/// timer, the way a PictureBox animates a GIF without anyone calling
/// <see cref="Image.SelectActiveFrame"/> by hand.
/// </summary>
/// <remarks>
/// Sealed with a private constructor rather than a C# static class, because
/// GDI+'s own ImageAnimator reflects as a sealed non-abstract class: an
/// application that checked typeof(ImageAnimator).IsAbstract must see the
/// same answer.
/// </remarks>
public sealed class ImageAnimator
{
    private ImageAnimator()
    {
    }

    // GDI+'s own tag for a GIF's per-frame delay, in hundredths of a second,
    // one 4-byte little-endian value per frame (gdiplusimaging.h's PropertyTagFrameDelay).
    private const int FrameDelayPropertyId = 0x5100;

    // GDI+ documents that a delay below this is raised to it: a 0 or
    // near-0 delay would otherwise spin the timer far faster than anything
    // on screen could show.
    private const int MinimumDelayMilliseconds = 100;

    private static readonly object Gate = new();
    private static readonly List<Animation> Animations = new();

    public static bool CanAnimate(Image? image) =>
        image is not null && image.GetFrameCount(FrameDimension.Time) > 1;

    public static void Animate(Image image, EventHandler onFrameChanged)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(onFrameChanged);

        if (!CanAnimate(image)) return;

        lock (Gate)
        {
            if (Animations.Exists(a => a.Image == image && a.Handler == onFrameChanged)) return;

            var delays = FrameDelaysMilliseconds(image);
            var animation = new Animation(image, onFrameChanged, delays);

            // The timer fires on a ThreadPool (background) thread, never the
            // thread that called Animate: UpdateFrames exists precisely so a
            // caller can bring the change back to its own thread when it needs to.
            animation.Timer = new Timer(_ => Tick(animation), null, delays[0], Timeout.Infinite);

            Animations.Add(animation);
        }
    }

    public static void StopAnimate(Image image, EventHandler onFrameChanged)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(onFrameChanged);

        Animation? found;
        lock (Gate)
        {
            found = Animations.Find(a => a.Image == image && a.Handler == onFrameChanged);
            if (found is not null) Animations.Remove(found);
        }

        found?.Timer?.Dispose();
    }

    /// <summary>Advances every animating image to the frame its timer last selected, on this thread.</summary>
    public static void UpdateFrames()
    {
        Animation[] snapshot;
        lock (Gate) { snapshot = Animations.ToArray(); }

        foreach (var animation in snapshot) UpdateFrame(animation);
    }

    /// <summary>Advances just one image, the same way, when it is the only one a caller cares about.</summary>
    public static void UpdateFrames(Image? image)
    {
        Animation[] snapshot;
        lock (Gate) { snapshot = Animations.Where(a => a.Image == image).ToArray(); }

        foreach (var animation in snapshot) UpdateFrame(animation);
    }

    private static void UpdateFrame(Animation animation) =>
        animation.Image.SelectActiveFrame(FrameDimension.Time, animation.CurrentFrame);

    private static void Tick(Animation animation)
    {
        bool stillRunning;
        lock (Gate)
        {
            stillRunning = Animations.Contains(animation);
            if (stillRunning)
            {
                animation.CurrentFrame = (animation.CurrentFrame + 1) % animation.FrameDelaysMilliseconds.Length;
                animation.Timer!.Change(animation.FrameDelaysMilliseconds[animation.CurrentFrame], Timeout.Infinite);
            }
        }

        if (stillRunning) animation.Handler(animation.Image, EventArgs.Empty);
    }

    /// <summary>
    /// One delay per frame, read from the GIF's own PropertyTagFrameDelay when
    /// it decoded one; a fixed minimum-delay frame for every frame otherwise,
    /// since nothing here currently fills that property in while decoding a GIF.
    /// </summary>
    private static int[] FrameDelaysMilliseconds(Image image)
    {
        var frameCount = image.GetFrameCount(FrameDimension.Time);

        PropertyItem item;
        try
        {
            item = image.GetPropertyItem(FrameDelayPropertyId);
        }
        catch (ArgumentException)
        {
            return Enumerable.Repeat(MinimumDelayMilliseconds, frameCount).ToArray();
        }

        var raw = item.Value;
        var delays = new int[frameCount];
        for (var i = 0; i < frameCount; i++)
        {
            var byteOffset = i * 4;
            var hundredths = byteOffset + 4 <= raw.Length ? BitConverter.ToInt32(raw, byteOffset) : 0;
            delays[i] = Math.Max(hundredths * 10, MinimumDelayMilliseconds);
        }

        return delays;
    }

    private sealed class Animation(Image image, EventHandler handler, int[] frameDelaysMilliseconds)
    {
        public Image Image { get; } = image;
        public EventHandler Handler { get; } = handler;
        public int[] FrameDelaysMilliseconds { get; } = frameDelaysMilliseconds;
        public int CurrentFrame { get; set; }
        public Timer? Timer { get; set; }
    }
}
