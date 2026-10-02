using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace System.Resources;

/// <summary>
/// A resource value that lives in a file next to the .resx instead of being
/// embedded in it: the &lt;value&gt; text is "fileName;typeName[;encoding]",
/// resolved relative to <see cref="ResXResourceReader.BasePath"/> when the
/// node is read.
/// </summary>
public class ResXFileRef
{
    public ResXFileRef(string fileName, string typeName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(typeName);

        FileName = fileName;
        TypeName = typeName;
    }

    public ResXFileRef(string fileName, string typeName, Encoding textFileEncoding) : this(fileName, typeName)
    {
        TextFileEncoding = textFileEncoding;
    }

    public string FileName { get; }

    public string TypeName { get; }

    public Encoding? TextFileEncoding { get; }

    /// <summary>"fileName;typeName" or "fileName;typeName;encoding", the shape .resx stores in &lt;value&gt;; a semicolon or quote in the file name is escaped by quoting the whole name, the way resgen does.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();

        var needsQuoting = FileName.Contains(';') || FileName.Contains('"');
        if (needsQuoting)
        {
            builder.Append('"').Append(FileName.Replace("\"", "\\\"")).Append('"');
        }
        else
        {
            builder.Append(FileName);
        }

        builder.Append(';').Append(TypeName);

        if (TextFileEncoding is not null)
        {
            builder.Append(';').Append(TextFileEncoding.WebName);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts the "fileName;typeName;encoding" text .resx stores for a file
    /// reference to and from a <see cref="ResXFileRef"/>; a ResXDataNode asks
    /// it for the actual value too, since that is where the file is read.
    /// </summary>
    public class Converter : TypeConverter
    {
        public Converter()
        {
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is not string text) return base.ConvertFrom(context, culture, value);

            var (fileName, typeName, encoding) = Parse(text);
            return new ResXFileRef(fileName, typeName, encoding ?? Encoding.Default);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is ResXFileRef fileRef) return fileRef.ToString();

            return base.ConvertTo(context, culture, value, destinationType);
        }

        /// <summary>Splits "fileName;typeName" or "fileName;typeName;encoding", honouring a quoted file name.</summary>
        internal static (string FileName, string TypeName, Encoding? Encoding) Parse(string text)
        {
            string fileName;
            string rest;

            if (text.Length > 0 && text[0] == '"')
            {
                var closing = text.IndexOf('"', 1);
                while (closing > 0 && closing < text.Length - 1 && text[closing + 1] == '"') closing = text.IndexOf('"', closing + 2);

                fileName = text[1..closing].Replace("\\\"", "\"");
                rest = text[(closing + 2)..]; // skip closing quote and the separating ';'
            }
            else
            {
                var firstSeparator = text.IndexOf(';');
                fileName = firstSeparator < 0 ? text : text[..firstSeparator];
                rest = firstSeparator < 0 ? string.Empty : text[(firstSeparator + 1)..];
            }

            var parts = rest.Split(';');
            var typeName = parts.Length > 0 ? parts[0] : string.Empty;
            Encoding? encoding = parts.Length > 1 && parts[1].Length > 0 ? Encoding.GetEncoding(parts[1]) : null;

            return (fileName, typeName, encoding);
        }
    }
}
