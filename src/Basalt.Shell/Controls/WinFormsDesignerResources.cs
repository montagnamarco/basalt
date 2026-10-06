using System.Drawing;
using System.Globalization;
using System.IO;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

/// <summary>Converts only built-in, textual resource values for the safe Forms preview.</summary>
internal static class WinFormsDesignerResources
{
    /// <summary>Writes only values the safe reader can reconstruct without loading a type.</summary>
    public static bool TryWrite(object? value, Type expectedType, out string text, out string? typeName)
    {
        ArgumentNullException.ThrowIfNull(expectedType);

        text = string.Empty;
        typeName = null;
        if (expectedType == typeof(string) && value is string stringValue)
        {
            text = stringValue;
            return true;
        }

        if (expectedType == typeof(int) && value is int integer)
        {
            text = integer.ToString(CultureInfo.InvariantCulture);
            typeName = "System.Int32, mscorlib";
            return true;
        }

        if (expectedType == typeof(bool) && value is bool boolean)
        {
            text = boolean ? "True" : "False";
            typeName = "System.Boolean, mscorlib";
            return true;
        }

        if (expectedType == typeof(Color) && value is Color color)
        {
            if (color.IsEmpty)
            {
                text = "Empty";
            }
            else if (color.IsKnownColor)
            {
                text = color.Name;
            }
            else if (color.IsNamedColor)
            {
                return false;
            }
            else
            {
                text = string.Create(CultureInfo.InvariantCulture,
                    $"{color.A}, {color.R}, {color.G}, {color.B}");
            }

            typeName = "System.Drawing.Color, System.Drawing";
            return true;
        }

        if (expectedType == typeof(Point) && value is Point point)
        {
            text = string.Create(CultureInfo.InvariantCulture, $"{point.X}, {point.Y}");
            typeName = "System.Drawing.Point, System.Drawing";
            return true;
        }

        if (expectedType == typeof(Size) && value is Size size)
        {
            text = string.Create(CultureInfo.InvariantCulture, $"{size.Width}, {size.Height}");
            typeName = "System.Drawing.Size, System.Drawing";
            return true;
        }

        return false;
    }

    public static bool TryRead(WinFormsResourceEntry entry, Type expectedType, out object? value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(expectedType);

        value = null;
        // An embedded Image or Icon is the one resource kind this reader accepts with
        // a mimetype attached: decoding a byte array into a picture runs no application
        // code, unlike the BinaryFormatter and SOAP mimetypes the rest of this method refuses.
        if ((expectedType == typeof(Image) || expectedType == typeof(Icon)) && TryReadImage(entry, expectedType, out value))
        {
            return true;
        }

        if (entry.MimeType is not null || !MatchesType(entry.TypeName, expectedType))
        {
            return false;
        }

        var text = entry.Value;
        if (expectedType == typeof(string))
        {
            value = text;
            return true;
        }

        if (expectedType == typeof(int)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            value = integer;
            return true;
        }

        if (expectedType == typeof(bool) && bool.TryParse(text, out var boolean))
        {
            value = boolean;
            return true;
        }

        if (expectedType == typeof(Color) && TryColor(text, out var color))
        {
            value = color;
            return true;
        }

        if (expectedType == typeof(Point) && TryPair(text, out var x, out var y))
        {
            value = new Point(x, y);
            return true;
        }

        if (expectedType == typeof(Size) && TryPair(text, out var width, out var height))
        {
            value = new Size(width, height);
            return true;
        }

        return false;
    }

    /// <summary>Decodes the raw file bytes an Image or Icon resx entry carries, the same way for an import or a round trip.</summary>
    private static bool TryReadImage(WinFormsResourceEntry entry, Type expectedType, out object? value)
    {
        value = null;
        if (!WinFormsImageResources.TryDecode(entry, out var bytes))
        {
            return false;
        }

        try
        {
            value = expectedType == typeof(Icon) ? new Icon(new MemoryStream(bytes)) : Image.FromStream(new MemoryStream(bytes));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool MatchesType(string? typeName, Type expectedType)
    {
        if (typeName is null)
        {
            return expectedType == typeof(string);
        }

        string[] allowedAssemblies;
        if (expectedType == typeof(string) || expectedType == typeof(int) || expectedType == typeof(bool))
        {
            allowedAssemblies = ["mscorlib", "System.Private.CoreLib", "System.Runtime"];
        }
        else if (expectedType == typeof(Color) || expectedType == typeof(Point) || expectedType == typeof(Size))
        {
            allowedAssemblies = ["System.Drawing", "System.Drawing.Primitives", "System.Drawing.Common"];
        }
        else
        {
            return false;
        }

        var separator = typeName.IndexOf(',');
        var declaredType = separator < 0 ? typeName.Trim() : typeName[..separator].Trim();
        if (!string.Equals(declaredType, expectedType.FullName, StringComparison.Ordinal))
        {
            return false;
        }

        if (separator < 0)
        {
            return true;
        }

        var assemblyStart = separator + 1;
        var assemblyEnd = typeName.IndexOf(',', assemblyStart);
        var assembly = assemblyEnd < 0 ? typeName[assemblyStart..].Trim()
            : typeName[assemblyStart..assemblyEnd].Trim();
        return allowedAssemblies.Contains(assembly, StringComparer.Ordinal);
    }

    private static bool TryPair(string text, out int first, out int second)
    {
        first = second = 0;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out first)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out second);
    }

    private static bool TryColor(string text, out Color color)
    {
        color = Color.Empty;
        var name = text.Trim();
        if (name.Equals("Empty", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Length > 0 && char.IsLetter(name[0])
            && Enum.TryParse<KnownColor>(name, ignoreCase: true, out var known)
            && Enum.IsDefined(known))
        {
            color = Color.FromKnownColor(known);
            return true;
        }

        if (name.StartsWith('#') && (name.Length == 7 || name.Length == 9)
            && uint.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = name.Length == 7
                ? Color.FromArgb(255, (int)(rgb >> 16), (int)((rgb >> 8) & 255), (int)(rgb & 255))
                : Color.FromArgb((int)(rgb >> 24), (int)((rgb >> 16) & 255),
                    (int)((rgb >> 8) & 255), (int)(rgb & 255));
            return true;
        }

        var parts = name.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not (3 or 4))
        {
            return false;
        }

        var channels = new byte[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!byte.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out channels[index]))
            {
                return false;
            }
        }

        color = parts.Length == 3
            ? Color.FromArgb(channels[0], channels[1], channels[2])
            : Color.FromArgb(channels[0], channels[1], channels[2], channels[3]);
        return true;
    }
}
