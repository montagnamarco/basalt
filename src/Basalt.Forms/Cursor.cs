using System.ComponentModel;
using System.Drawing;
using System.Runtime.Serialization;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Input;

namespace System.Windows.Forms;

/// <summary>
/// A mouse pointer shape. The standard ones are the system's own pointers;
/// a cursor loaded from a file or a stream keeps its image, and is shown as
/// the arrow until custom pointers are supported.
/// </summary>
[TypeConverter(typeof(CursorConverter))]
[Serializable]
public sealed class Cursor : IDisposable, ISerializable
{
    private static int s_nextHandle = 0x20000;

    private readonly IntPtr _handle;
    private readonly bool _owned;

    internal Cursor(StandardCursorType standard, string name)
    {
        Standard = standard;
        Name = name;
        _handle = NextHandle();
    }

    /// <summary>A cursor from a native handle cannot be read here.</summary>
    public Cursor(IntPtr handle)
    {
        if (handle == IntPtr.Zero) throw new ArgumentException("Win32 handle that was passed to Cursor is not valid or is the wrong type.", nameof(handle));

        throw new PlatformNotSupportedException("Cursors cannot be created from native handles: there are no Win32 cursors on this platform.");
    }

    public Cursor(string fileName)
        : this(File.OpenRead(fileName))
    {
    }

    public Cursor(Type type, string resource)
        : this(type.Assembly.GetManifestResourceStream(type, resource) ?? throw new ArgumentException($"Resource '{resource}' cannot be found in class '{type.FullName}'.", nameof(resource)))
    {
    }

    /// <summary>Reads a .cur or .ico file; its image is kept for drawing.</summary>
    public Cursor(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using (stream)
        {
            using var icon = new Icon(stream);
            Image = icon.ToBitmap();
        }

        Standard = StandardCursorType.Arrow;
        Name = string.Empty;
        _handle = NextHandle();
        _owned = true;
    }

    internal StandardCursorType Standard { get; }

    internal string Name { get; }

    internal Bitmap? Image { get; private set; }

    public IntPtr Handle => _handle;

    public IntPtr CopyHandle() => _handle;

    public Point HotSpot => Point.Empty;

    public Size Size => Image?.Size ?? new Size(32, 32);

    public object? Tag { get; set; }

    /// <summary>The cursor shown over every window of the application, overriding the controls' own; null for theirs.</summary>
    public static Cursor? Current
    {
        get => s_current;
        set
        {
            s_current = value;
            FormsInput.RefreshCursor();
        }
    }

    private static Cursor? s_current;

    /// <summary>
    /// The pointer's position on the screen. Moving it is ignored rather than
    /// refused: the platforms do not let applications warp the pointer, and
    /// code that recentres it should go on working.
    /// </summary>
    public static Point Position
    {
        get => FormsInput.ScreenPosition;
        set
        {
        }
    }

    /// <summary>Confining the pointer is not available; the clip is the whole screen.</summary>
    public static Rectangle Clip
    {
        get => Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
        set
        {
        }
    }

    public static void Hide()
    {
    }

    public static void Show()
    {
    }

    public void Draw(Graphics g, Rectangle targetRect)
    {
        ArgumentNullException.ThrowIfNull(g);

        if (Image is { } image) g.DrawImage(image, targetRect.X, targetRect.Y, image.Width, image.Height);
    }

    public void DrawStretched(Graphics g, Rectangle targetRect)
    {
        ArgumentNullException.ThrowIfNull(g);

        if (Image is { } image) g.DrawImage(image, targetRect);
    }

    public void Dispose()
    {
        if (_owned)
        {
            Image?.Dispose();
            Image = null;
        }
    }

    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) =>
        throw new PlatformNotSupportedException("Cursors cannot be serialized on this platform.");

    public override bool Equals(object? obj) => obj is Cursor other && other._handle == _handle;

    public override int GetHashCode() => _handle.GetHashCode();

    public static bool operator ==(Cursor? left, Cursor? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Cursor? left, Cursor? right) => !(left == right);

    public override string ToString() => string.IsNullOrEmpty(Name) ? "[Cursor: System.Windows.Forms.Cursor]" : $"[Cursor: {Name}]";

    private static IntPtr NextHandle() => new(Interlocked.Add(ref s_nextHandle, 2));
}
