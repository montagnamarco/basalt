namespace System.Drawing.Drawing2D;

/// <summary>
/// An opaque token for the state Graphics.Save captured: it names a point in
/// that Graphics's save stack and carries no public data of its own, exactly
/// as GDI+'s handle does.
/// </summary>
public sealed class GraphicsState : MarshalByRefObject
{
    internal GraphicsState(long id) => Id = id;

    internal long Id { get; }
}
