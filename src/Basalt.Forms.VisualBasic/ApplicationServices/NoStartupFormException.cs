using System.Runtime.Serialization;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>Thrown by Run when OnStartup did not cancel but no main form was ever assigned.</summary>
[Serializable]
public class NoStartupFormException : Exception
{
    public NoStartupFormException()
        : base("The startup form for the application was not specified.")
    {
    }

    public NoStartupFormException(string message)
        : base(message)
    {
    }

    public NoStartupFormException(string message, Exception inner)
        : base(message, inner)
    {
    }

#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete; kept only because Microsoft's own reference assembly still declares this constructor.
    [Obsolete("Type or member is obsolete", DiagnosticId = "SYSLIB0051")]
    protected NoStartupFormException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
#pragma warning restore SYSLIB0051
}
