using System.Runtime.Serialization;

namespace System.Drawing.Printing;

/// <summary>
/// What GDI+ throws when a PrinterSettings does not describe a printer that
/// can actually be used: here, there is never a real printer to find, so this
/// is thrown whenever printing is attempted without redirecting to a file.
/// </summary>
[Serializable]
public class InvalidPrinterException : SystemException
{
    public InvalidPrinterException(PrinterSettings settings)
        : base(Describe(settings))
    {
    }

    protected InvalidPrinterException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context) => base.GetObjectData(info, context);

    private static string Describe(PrinterSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var name = string.IsNullOrEmpty(settings.PrinterName) ? "(none)" : settings.PrinterName;
        return $"Settings to access printer '{name}' are not valid.";
    }
}
