namespace Basalt.Core.Features;

/// <summary>
/// The features a build of the IDE shows, chosen once when its window is made.
///
/// A feature that is hidden is not deleted: its code stays in the repository,
/// builds, and is tested. The switch only decides what a user is offered.
/// </summary>
/// <remarks>
/// An immutable record passed to the window, the view model and the language
/// catalogue, rather than a static flag. Parallel tests each build their own
/// window with their own value, so one test's setting cannot leak into another.
/// </remarks>
public sealed record IdeFeatures
{
    /// <summary>What a user sees: no legacy dialect is offered.</summary>
    public static IdeFeatures Default { get; } = new();

    /// <summary>
    /// Visual Basic 6 (.vbp, .frm, the converter and the form designer for
    /// them) and QuickBASIC (.bas, its language, its Run and its interpreter
    /// debugger). Off by default; the code behind it is unchanged.
    /// </summary>
    public bool LegacyBasic { get; init; }
}
