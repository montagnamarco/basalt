using System.Runtime.Serialization;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>
/// Thrown when a single-instance application cannot forward its command line
/// to the already-running instance (the lock file exists but nothing answers
/// the pipe behind it, for example because that process is exiting).
/// </summary>
[Serializable]
public class CantStartSingleInstanceException : Exception
{
    public CantStartSingleInstanceException()
        : base("Could not start a single-instance application.")
    {
    }

    public CantStartSingleInstanceException(string message)
        : base(message)
    {
    }

    public CantStartSingleInstanceException(string message, Exception inner)
        : base(message, inner)
    {
    }

#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete; kept only because Microsoft's own reference assembly still declares this constructor.
    [Obsolete("Type or member is obsolete", DiagnosticId = "SYSLIB0051")]
    protected CantStartSingleInstanceException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
#pragma warning restore SYSLIB0051
}
