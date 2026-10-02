namespace System.Windows.Forms;

public abstract partial class AxHost
{
    /// <summary>
    /// On Windows, advises a COM connection point so a managed sink gets an
    /// ActiveX control's events, and revokes it on <see cref="Disconnect"/>
    /// or finalization. There is no COM connection point to advise here, so
    /// the constructor - the step that would call IConnectionPoint.Advise -
    /// refuses; nothing is ever connected, so disconnecting is a no-op.
    /// </summary>
    public class ConnectionPointCookie
    {
        private const string NoConnectionPoint =
            "There is no COM connection point behind this control: ConnectionPointCookie has nothing to advise.";

        public ConnectionPointCookie(object source, object sink, Type eventInterface)
        {
            throw new PlatformNotSupportedException(NoConnectionPoint);
        }

        public void Disconnect()
        {
        }

        ~ConnectionPointCookie()
        {
        }
    }
}
