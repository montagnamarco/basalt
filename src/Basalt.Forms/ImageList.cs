using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Threading;

namespace System.Windows.Forms;

/// <summary>
/// A set of same-size pictures that a ListView, TreeView, ToolBar, TabControl
/// or button looks up by index or key, drawn once and shared instead of
/// loaded again by every control that uses them.
/// </summary>
[DefaultProperty("Images")]
public sealed class ImageList : Component
{
    /// <summary>What Images[index] returns before ImageSize is ever set.</summary>
    private const int DefaultDimension = 16;

    /// <summary>The widest or tallest a stored picture may be, as ImageList enforces.</summary>
    private const int MaxDimension = 256;

    private static long _nextHandle;

    private Size _imageSize = new(DefaultDimension, DefaultDimension);
    private ColorDepth _colorDepth = ColorDepth.Depth8Bit;
    private Color _transparentColor = Color.Transparent;
    private IntPtr _handle;
    private bool _handleCreated;

    public ImageList()
    {
        Images = new ImageCollection(this);
    }

    public ImageList(IContainer container)
        : this()
    {
        ArgumentNullException.ThrowIfNull(container);

        container.Add(this);
    }

    /// <summary>
    /// How many bits of colour the native list would keep per pixel. Basalt
    /// always stores true-colour ARGB, so this only round-trips through
    /// ImageStream and decides whether changing it recreates the handle.
    /// </summary>
    [DefaultValue(ColorDepth.Depth8Bit)]
    public ColorDepth ColorDepth
    {
        get => _colorDepth;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ColorDepth));

            if (value == _colorDepth) return;

            _colorDepth = value;
            RecreateHandleIfNeeded();
        }
    }

    /// <summary>A unique value standing in for the native image list handle: nothing here calls Win32.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public IntPtr Handle
    {
        get
        {
            if (!_handleCreated) CreateHandle();

            return _handle;
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public bool HandleCreated => _handleCreated;

    /// <summary>The size every stored picture is resized to, 16 by 16 until set, never over 256 by 256.</summary>
    [Localizable(true)]
    public Size ImageSize
    {
        get => _imageSize;
        set
        {
            if (value.Width is < 1 or > MaxDimension || value.Height is < 1 or > MaxDimension)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"ImageSize values must be between 1 and {MaxDimension} inclusive.");
            }

            if (value == _imageSize) return;

            _imageSize = value;
            Images.ResizeAll(value);
            RecreateHandleIfNeeded();
        }
    }

    /// <summary>
    /// An in-memory snapshot of this list's pictures and settings. Null when
    /// the list is empty, as Microsoft's designer leaves nothing to write to
    /// a .resx for an ImageList with no pictures.
    /// </summary>
    [Browsable(false)]
    [DefaultValue(null)]
    public ImageListStreamer? ImageStream
    {
        get => Images.Empty ? null : new ImageListStreamer(this);
        set => value?.CopyTo(this);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ImageCollection Images { get; }

    [Browsable(false)]
    [DefaultValue(null)]
    public object? Tag { get; set; }

    /// <summary>
    /// The colour a stored picture's matching pixels are turned fully
    /// transparent on, applied when the picture is added. Transparent (the
    /// default) is a sentinel meaning "none": a picture's own alpha channel
    /// is left alone, as Windows leaves a true-colour icon alone.
    /// </summary>
    public Color TransparentColor
    {
        get => _transparentColor;
        set => _transparentColor = value;
    }

    /// <summary>Raised when ImageSize or ColorDepth changes while the handle already exists.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public event EventHandler? RecreateHandle;

    protected override void Dispose(bool disposing)
    {
        if (disposing) Images.Clear();

        base.Dispose(disposing);
    }

    public void Draw(Graphics g, Point pt, int index) => Draw(g, pt.X, pt.Y, index);

    public void Draw(Graphics g, int x, int y, int index) => Draw(g, x, y, _imageSize.Width, _imageSize.Height, index);

    public void Draw(Graphics g, int x, int y, int width, int height, int index)
    {
        ArgumentNullException.ThrowIfNull(g);

        if (index < 0 || index >= Images.Count) throw new ArgumentOutOfRangeException(nameof(index));

        using var image = Images[index];
        g.DrawImage(image, x, y, width, height);
    }

    public override string ToString() => $"{base.ToString()} Images.Count: {Images.Count}, ImageSize: {ImageSize}";

    /// <summary>
    /// Hands this list the pictures and settings an ImageStream setter or a
    /// handle recreation read, replacing what it held.
    /// </summary>
    internal void ReplaceImages(IReadOnlyList<Bitmap> images, IReadOnlyList<string> keys, Size imageSize, ColorDepth colorDepth, Color transparentColor)
    {
        _imageSize = imageSize;
        _colorDepth = colorDepth;
        _transparentColor = transparentColor;
        Images.ReplaceAll(images, keys);
        RecreateHandleIfNeeded();
    }

    private void CreateHandle()
    {
        _handle = new IntPtr(Interlocked.Increment(ref _nextHandle));
        _handleCreated = true;
    }

    /// <summary>
    /// A size or colour-depth change that Windows would ask the native list
    /// to rebuild for: this gives out a new fake handle and tells listeners,
    /// but only if something already asked for a handle to rebuild.
    /// </summary>
    private void RecreateHandleIfNeeded()
    {
        if (!_handleCreated) return;

        CreateHandle();
        RecreateHandle?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The pictures of an ImageList, looked up by position or by an optional key.</summary>
    public sealed class ImageCollection : ICollection, IEnumerable, IList
    {
        private readonly ImageList _owner;
        private readonly List<Bitmap> _images = [];
        private readonly List<string> _keys = [];

        internal ImageCollection(ImageList owner) => _owner = owner;

        [Browsable(false)]
        public int Count => _images.Count;

        public bool Empty => _images.Count == 0;

        public bool IsReadOnly => false;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Image this[int index]
        {
            get => Copy(index);
            set
            {
                CheckIndex(index);
                ArgumentNullException.ThrowIfNull(value);

                _images[index].Dispose();
                _images[index] = Prepare(value, _owner.TransparentColor);
            }
        }

        public Image? this[string key]
        {
            get
            {
                var index = IndexOfKey(key);
                return index < 0 ? null : Copy(index);
            }
        }

        public StringCollection Keys
        {
            get
            {
                var keys = new StringCollection();
                foreach (var key in _keys)
                {
                    if (key.Length > 0) keys.Add(key);
                }

                return keys;
            }
        }

        public void Add(string key, Image image)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(image);

            Store(Prepare(image, _owner.TransparentColor), key);
        }

        public void Add(string key, Icon icon)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(icon);

            Store(Prepare(icon.ToBitmap(), _owner.TransparentColor), key);
        }

        public void Add(Icon value)
        {
            ArgumentNullException.ThrowIfNull(value);

            Store(Prepare(value.ToBitmap(), _owner.TransparentColor), string.Empty);
        }

        public void Add(Image value)
        {
            ArgumentNullException.ThrowIfNull(value);

            Store(Prepare(value, _owner.TransparentColor), string.Empty);
        }

        /// <summary>Adds a picture made transparent with a colour of its own, not the list's TransparentColor.</summary>
        public int Add(Image value, Color transparentColor)
        {
            ArgumentNullException.ThrowIfNull(value);

            var index = Count;
            Store(Prepare(value, transparentColor), string.Empty);
            return index;
        }

        public void AddRange(params Image[] images)
        {
            ArgumentNullException.ThrowIfNull(images);

            foreach (var image in images) Add(image);
        }

        /// <summary>Slices a filmstrip picture, as wide as a whole number of ImageSize-wide frames, into separate pictures.</summary>
        public int AddStrip(Image value)
        {
            ArgumentNullException.ThrowIfNull(value);

            var size = _owner.ImageSize;
            if (value.Width == 0 || value.Height != size.Height || value.Width % size.Width != 0)
            {
                throw new ArgumentException(
                    $"The image's height must equal ImageSize.Height ({size.Height}) and its width a multiple of ImageSize.Width ({size.Width}).",
                    nameof(value));
            }

            var firstIndex = Count;
            using var source = new Bitmap(value);
            var count = value.Width / size.Width;
            for (var i = 0; i < count; i++)
            {
                using var piece = source.Clone(new Rectangle(i * size.Width, 0, size.Width, size.Height), source.PixelFormat);
                Store(Prepare(piece, _owner.TransparentColor), string.Empty);
            }

            return firstIndex;
        }

        public void Clear()
        {
            foreach (var image in _images) image.Dispose();

            _images.Clear();
            _keys.Clear();
        }

        /// <summary>
        /// Always false: Microsoft never finished content-based lookup for
        /// ImageList either, and warns it may disappear; nothing here
        /// compares pictures pixel by pixel to answer it.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool Contains(Image image) => false;

        public bool ContainsKey(string key) => IndexOfKey(key) >= 0;

        public IEnumerator GetEnumerator()
        {
            for (var i = 0; i < _images.Count; i++) yield return Copy(i);
        }

        /// <summary>Always -1, for the reason <see cref="Contains"/> is always false.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public int IndexOf(Image image) => -1;

        public int IndexOfKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;

            for (var i = 0; i < _keys.Count; i++)
            {
                if (string.Equals(_keys[i], key, StringComparison.OrdinalIgnoreCase)) return i;
            }

            return -1;
        }

        /// <summary>Not supported: an ImageList has no notion of "this picture", only a position or a key.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Remove(Image image) =>
            throw new NotSupportedException("ImageList cannot remove a picture by reference; remove it by index or by key.");

        public void RemoveAt(int index)
        {
            CheckIndex(index);

            _images[index].Dispose();
            _images.RemoveAt(index);
            _keys.RemoveAt(index);
        }

        public void RemoveByKey(string key)
        {
            var index = IndexOfKey(key);
            if (index >= 0) RemoveAt(index);
        }

        public void SetKeyName(int index, string name)
        {
            CheckIndex(index);
            ArgumentNullException.ThrowIfNull(name);

            _keys[index] = name;
        }

        /// <summary>Resizes every stored picture after ImageSize changes, as the native list would on the next draw.</summary>
        internal void ResizeAll(Size size)
        {
            for (var i = 0; i < _images.Count; i++)
            {
                var resized = new Bitmap(_images[i], size);
                _images[i].Dispose();
                _images[i] = resized;
            }
        }

        internal List<Bitmap> CloneBitmaps()
        {
            var clones = new List<Bitmap>(_images.Count);
            foreach (var image in _images) clones.Add(new Bitmap(image));

            return clones;
        }

        internal List<string> CloneKeys() => [.. _keys];

        /// <summary>Replaces every picture and key, taking ownership of the bitmaps given (no further copy).</summary>
        internal void ReplaceAll(IReadOnlyList<Bitmap> images, IReadOnlyList<string> keys)
        {
            Clear();
            for (var i = 0; i < images.Count; i++)
            {
                _images.Add(images[i]);
                _keys.Add(keys[i]);
            }
        }

        private Image Copy(int index)
        {
            CheckIndex(index);

            return (Image)_images[index].Clone();
        }

        private void Store(Bitmap image, string key)
        {
            _images.Add(image);
            _keys.Add(key);
        }

        /// <summary>
        /// A picture ready to be stored: made transparent on its own size
        /// first, so resizing never blurs the colour key into its edges,
        /// then resized to ImageSize.
        /// </summary>
        private Bitmap Prepare(Image source, Color transparentColor)
        {
            var bitmap = new Bitmap(source);
            if (transparentColor != Color.Transparent) bitmap.MakeTransparent(transparentColor);

            if (bitmap.Size == _owner.ImageSize) return bitmap;

            var resized = new Bitmap(bitmap, _owner.ImageSize);
            bitmap.Dispose();
            return resized;
        }

        private void CheckIndex(int index)
        {
            if (index < 0 || index >= _images.Count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        // The rest is ICollection, IEnumerable and IList plumbing that a
        // binary never calls directly (WinForms applications use the typed
        // members above); it exists only because Microsoft's ImageCollection
        // declares those interfaces.

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        void ICollection.CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);

            for (var i = 0; i < _images.Count; i++) array.SetValue(Copy(i), index + i);
        }

        bool IList.IsFixedSize => false;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (Image)value!;
        }

        int IList.Add(object? value)
        {
            Add((Image)value!);
            return Count - 1;
        }

        bool IList.Contains(object? value) => value is Image image && Contains(image);

        int IList.IndexOf(object? value) => value is Image image ? IndexOf(image) : -1;

        void IList.Insert(int index, object? value) =>
            throw new NotSupportedException("ImageList does not support inserting a picture at a position; use Add.");

        void IList.Remove(object? value)
        {
            if (value is Image image) Remove(image);
        }
    }
}
