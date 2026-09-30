using System.Diagnostics.CodeAnalysis;

namespace System.Drawing.Imaging;

/// <summary>
/// Which axis a multi-frame image's frames vary along: time (an animated GIF),
/// page or resolution. Identified by the same GUIDs GDI+ uses, published in the
/// Windows SDK's gdiplusimaging.h.
/// </summary>
public sealed class FrameDimension
{
    public FrameDimension(Guid guid) => Guid = guid;

    public Guid Guid { get; }

    public static FrameDimension Page { get; } = new(new Guid("7462dc86-6180-4c7e-8e3f-ee7333a7a483"));

    public static FrameDimension Resolution { get; } = new(new Guid("84236f7b-3bd3-428f-8dab-4ea1439ca315"));

    public static FrameDimension Time { get; } = new(new Guid("6aedbd6d-3fb5-418a-83a6-7f45229dc872"));

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FrameDimension other && other.Guid == Guid;

    public override int GetHashCode() => Guid.GetHashCode();

    public override string ToString() => $"[FrameDimension: {Guid}]";
}
