namespace Basalt.Razor.Vb;

/// <summary>
/// The few things netstandard2.0 does not have.
///
/// This assembly ships inside the NuGet package, so it cannot target
/// something newer; writing them out once is better than a private copy in
/// every file that needs one.
/// </summary>
internal static class Portable
{
    /// <summary>Keeps a number inside a range. Math.Clamp is not available.</summary>
    public static int Clamp(int value, int low, int high) =>
        value < low ? low : value > high ? high : value;
}
