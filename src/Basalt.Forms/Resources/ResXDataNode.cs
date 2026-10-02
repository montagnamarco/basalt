using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace System.Resources;

/// <summary>
/// One &lt;data&gt; or &lt;metadata&gt; element of a .resx file: a name, a
/// comment, and either a value already in hand, a file reference, or (when a
/// ResXResourceReader built the node with UseResXDataNodes) the raw XML text
/// and type name, resolved into a real value only when GetValue is called.
/// </summary>
public sealed class ResXDataNode : ISerializable
{
    // The object was handed to us directly (not parsed from XML): GetValue
    // always returns it, whatever resolver is passed.
    private readonly bool _hasDirectValue;
    private readonly object? _directValue;

    // Or it is a ResXFileRef the caller built directly, instead of parsing one.
    private readonly ResXFileRef? _directFileRef;

    // Or it came from a ResXResourceReader: the raw <value> text, the type and
    // mimetype attributes, and where to resolve a relative file reference.
    private readonly string? _rawValue;
    private readonly string? _rawTypeName;
    private readonly string? _mimeType;
    private readonly string? _basePath;

    private readonly Func<Type, string>? _typeNameConverter;
    private readonly Point _position;

    public ResXDataNode(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        _hasDirectValue = true;
        _directValue = value;
    }

    public ResXDataNode(string name, object? value, Func<Type, string>? typeNameConverter) : this(name, value)
    {
        _typeNameConverter = typeNameConverter;
    }

    public ResXDataNode(string name, ResXFileRef fileRef)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fileRef);

        Name = name;
        _directFileRef = fileRef;
    }

    public ResXDataNode(string name, ResXFileRef fileRef, Func<Type, string>? typeNameConverter) : this(name, fileRef)
    {
        _typeNameConverter = typeNameConverter;
    }

    /// <summary>Built by ResXResourceReader while parsing a &lt;data&gt;/&lt;metadata&gt; element; the value stays raw text until GetValue asks for it.</summary>
    internal ResXDataNode(string name, string? rawValue, string? typeName, string? mimeType, string? comment, Point position, string? basePath)
    {
        Name = name;
        _rawValue = rawValue;
        _rawTypeName = typeName;
        _mimeType = mimeType;
        Comment = comment ?? string.Empty;
        _position = position;
        _basePath = basePath;
    }

    public string Name { get; set; }

    public string Comment { get; set; } = string.Empty;

    /// <summary>The file this node points at, or null when the value is embedded or not a file reference.</summary>
    public ResXFileRef? FileRef => _directFileRef ?? (IsXmlFileRef ? ParseRawFileRef() : null);

    private bool IsXmlFileRef => _rawTypeName is not null && ResolveType(_rawTypeName, null, null) == typeof(ResXFileRef);

    public Point GetNodePosition() => _position;

    public object? GetValue(ITypeResolutionService? typeResolver) => GetValueCore(typeResolver, null);

    public object? GetValue(AssemblyName[]? names) => GetValueCore(null, names);

    public string? GetValueTypeName(ITypeResolutionService? typeResolver) => GetValueTypeNameCore(typeResolver, null);

    public string? GetValueTypeName(AssemblyName[]? names) => GetValueTypeNameCore(null, names);

    private object? GetValueCore(ITypeResolutionService? resolver, AssemblyName[]? names)
    {
        if (_hasDirectValue) return _directValue;

        if (_directFileRef is not null) return LoadFileRef(_directFileRef, resolver, names, basePath: null);

        var type = ResolveType(_rawTypeName, resolver, names) ?? typeof(string);

        if (type == typeof(ResXFileRef))
        {
            return LoadFileRef(ParseRawFileRef(), resolver, names, _basePath);
        }

        if (IsBinaryFormatterMimeType(_mimeType))
        {
            throw new NotSupportedException(
                $"The resource '{Name}' was saved with mimetype '{_mimeType}' (a BinaryFormatter payload). " +
                "Basalt does not deserialize BinaryFormatter data; the raw bytes are kept as written but no object value can be produced.");
        }

        if (type == typeof(byte[])) return Convert.FromBase64String(_rawValue ?? string.Empty);

        if (type == typeof(string)) return _rawValue ?? string.Empty;

        var converter = TypeDescriptor.GetConverter(type);
        if (converter.CanConvertFrom(typeof(string))) return converter.ConvertFromInvariantString(_rawValue ?? string.Empty);

        throw new NotSupportedException($"The resource '{Name}' has type '{type}', which has no TypeConverter that can parse its stored text.");
    }

    private static object LoadFileRef(ResXFileRef fileRef, ITypeResolutionService? resolver, AssemblyName[]? names, string? basePath)
    {
        var valueType = ResolveType(fileRef.TypeName, resolver, names) ?? typeof(string);
        var path = ResXFileRefLoader.ResolvePath(fileRef.FileName, basePath);
        return ResXFileRefLoader.Load(path, valueType, fileRef.TextFileEncoding);
    }

    private string? GetValueTypeNameCore(ITypeResolutionService? resolver, AssemblyName[]? names)
    {
        if (_hasDirectValue)
        {
            var type = _directValue?.GetType() ?? typeof(object);
            return _typeNameConverter?.Invoke(type) ?? type.AssemblyQualifiedName;
        }

        if (_directFileRef is not null) return _typeNameConverter?.Invoke(typeof(ResXFileRef)) ?? typeof(ResXFileRef).AssemblyQualifiedName;

        if (_rawTypeName is null) return typeof(string).AssemblyQualifiedName;

        var resolved = ResolveType(_rawTypeName, resolver, names);
        return resolved is not null ? (_typeNameConverter?.Invoke(resolved) ?? resolved.AssemblyQualifiedName) : _rawTypeName;
    }

    private ResXFileRef ParseRawFileRef()
    {
        var (fileName, typeName, encoding) = ResXFileRef.Converter.Parse(_rawValue ?? string.Empty);
        return encoding is null ? new ResXFileRef(fileName, typeName) : new ResXFileRef(fileName, typeName, encoding);
    }

    /// <summary>The four mimetypes ResXResourceWriter stamps on a BinaryFormatter-serialized value; see ResXResourceWriter's matching constants.</summary>
    private static bool IsBinaryFormatterMimeType(string? mimeType) => mimeType is not null &&
        (mimeType == ResXResourceWriter.BinSerializedObjectMimeType ||
         mimeType == ResXResourceWriter.ByteArraySerializedObjectMimeType ||
         mimeType == ResXResourceWriter.SoapSerializedObjectMimeType ||
         mimeType == ResXResourceWriter.DefaultSerializedObjectMimeType);

    /// <summary>Resolves a type name the way a resx reader does: through the resolution service first, then by assembly-qualified name, then by searching the given assemblies, then whatever is already loaded.</summary>
    internal static Type? ResolveType(string? typeName, ITypeResolutionService? resolver, AssemblyName[]? names)
    {
        if (string.IsNullOrEmpty(typeName)) return null;

        if (resolver is not null)
        {
            var resolved = resolver.GetType(typeName);
            if (resolved is not null) return resolved;
        }

        var direct = Type.GetType(typeName, throwOnError: false);
        if (direct is not null) return direct;

        var simpleName = typeName.Split(',')[0].Trim();

        if (names is not null)
        {
            foreach (var assemblyName in names)
            {
                try
                {
                    var assembly = Assembly.Load(assemblyName);
                    var type = assembly.GetType(simpleName, throwOnError: false);
                    if (type is not null) return type;
                }
                catch (IOException)
                {
                }
                catch (BadImageFormatException)
                {
                }
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(simpleName, throwOnError: false);
            if (type is not null) return type;
        }

        return null;
    }

    /// <summary>Minimal but real: enough fields to reconstruct the node, since BinaryFormatter-style full fidelity is not a goal here.</summary>
    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);

        info.AddValue("Name", Name);
        info.AddValue("Comment", Comment);
        info.AddValue("TypeName", _rawTypeName);
        info.AddValue("MimeType", _mimeType);
        info.AddValue("Value", _hasDirectValue ? _directValue : _rawValue);
    }
}
