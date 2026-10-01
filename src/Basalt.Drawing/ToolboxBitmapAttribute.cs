namespace System.Drawing;

/// <summary>
/// Names the 16x16 bitmap a designer's toolbox shows for a component: an
/// embedded resource named after the type, the way Visual Studio's own
/// "Embedded Resource" build action for a component's .bmp or .png works.
/// </summary>
public class ToolboxBitmapAttribute : Attribute
{
    private readonly Image? _literalImage;
    private readonly string? _resourceName;

    /// <summary>The attribute a component without one of its own is treated as having.</summary>
    public static readonly ToolboxBitmapAttribute Default = new();

    private ToolboxBitmapAttribute()
    {
    }

    /// <summary>A bitmap loaded directly from a file on disk, rather than an embedded resource.</summary>
    public ToolboxBitmapAttribute(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        _literalImage = new Bitmap(fileName);
    }

    public ToolboxBitmapAttribute(Type t)
    {
        ArgumentNullException.ThrowIfNull(t);

        _resourceName = t.Name;
    }

    public ToolboxBitmapAttribute(Type t, string name)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(name);

        _resourceName = name;
    }

    public override bool Equals(object? obj) =>
        obj is ToolboxBitmapAttribute other
        && other._resourceName == _resourceName
        && ReferenceEquals(other._literalImage, _literalImage);

    public override int GetHashCode() => HashCode.Combine(_resourceName, _literalImage);

    public Image GetImage(object component)
    {
        ArgumentNullException.ThrowIfNull(component);

        return GetImage(component.GetType(), largeImage: false);
    }

    public Image GetImage(object component, bool largeImage)
    {
        ArgumentNullException.ThrowIfNull(component);

        return GetImage(component.GetType(), largeImage);
    }

    public Image GetImage(Type type) => GetImage(type, largeImage: false);

    public Image GetImage(Type type, bool largeImage)
    {
        ArgumentNullException.ThrowIfNull(type);

        return _literalImage ?? GetImage(type, _resourceName, largeImage);
    }

    /// <summary>
    /// GDI+ keeps one toolbox bitmap size (16x16); <paramref name="largeImage"/>
    /// exists only so code written against a 32-bit-icon-capable designer still
    /// compiles, and is accepted without changing which image comes back.
    /// </summary>
    public Image GetImage(Type type, string? imageName, bool largeImage)
    {
        ArgumentNullException.ThrowIfNull(type);

        return GetImageFromResource(type, imageName, largeImage);
    }

    /// <summary>
    /// Looks for an embedded resource named "&lt;imageName ?? type.Name&gt;.bmp"
    /// or "...png" anywhere in the type's own assembly, the way Visual Studio
    /// names a component's toolbox bitmap resource; falls back to a blank
    /// 16x16 image when there is none.
    /// </summary>
    public static Image GetImageFromResource(Type t, string? imageName, bool largeImage)
    {
        ArgumentNullException.ThrowIfNull(t);

        var baseName = imageName ?? t.Name;
        var assembly = t.Assembly;
        var names = assembly.GetManifestResourceNames();

        foreach (var extension in new[] { "bmp", "png" })
        {
            var suffix = "." + baseName + "." + extension;
            var match = Array.Find(names, candidate => candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                ?? Array.Find(names, candidate => candidate.Equals(baseName + "." + extension, StringComparison.OrdinalIgnoreCase));

            if (match is null) continue;

            using var stream = assembly.GetManifestResourceStream(match);
            if (stream is not null) return new Bitmap(stream);
        }

        return CreateDefaultImage();
    }

    private static Bitmap CreateDefaultImage() => new(16, 16);
}
