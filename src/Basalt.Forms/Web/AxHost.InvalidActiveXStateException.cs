namespace System.Windows.Forms;

public abstract partial class AxHost
{
    /// <summary>Thrown when an ActiveX control's property or method is reached before it is ready to answer.</summary>
    public class InvalidActiveXStateException : Exception
    {
        private readonly ActiveXInvokeKind _kind;
        private readonly bool _hasKind;

        public InvalidActiveXStateException()
        {
        }

        public InvalidActiveXStateException(string? name, ActiveXInvokeKind kind)
            : base(name)
        {
            _kind = kind;
            _hasKind = true;
        }

        public override string ToString() =>
            _hasKind ? $"{base.ToString()} ({_kind})" : base.ToString();
    }
}
