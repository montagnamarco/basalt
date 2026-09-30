namespace System.Drawing.Drawing2D;

/// <summary>
/// An opaque token for the state Graphics.BeginContainer captured: like
/// GraphicsState, it names a point in the save stack and carries no public
/// data of its own.
/// </summary>
public sealed class GraphicsContainer : MarshalByRefObject
{
    internal GraphicsContainer(long id) => Id = id;

    internal long Id { get; }
}
