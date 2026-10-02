using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>
/// The scenarios compared, pixel by pixel, between a control painted by Microsoft's own
/// System.Windows.Forms (classic theme, recorded on real Windows) and the same control painted
/// by Basalt.Forms.
/// </summary>
/// <remarks>
/// Like Scenarios.cs, this file (and its VisualScenarios.*.cs siblings) is compiled twice: into
/// the Windows recorder, against Microsoft's System.Windows.Forms, and into the tests, against
/// ours. Each scenario fixes everything that could otherwise differ between two unrelated
/// renders: the font, the colours, the values and selections, and which states are shown. No
/// scenario depends on focus, hover or mouse capture, because a headless render here and a
/// recording made without a running message loop on Windows cannot be made to agree on those.
/// </remarks>
public static partial class VisualScenarios
{
    /// <summary>
    /// One control, rendered at a fixed size by <see cref="Create"/>. <see cref="TextRectangles"/>
    /// are, in the control's own coordinates, the areas where text glyphs are painted: GDI and
    /// Skia rasterise glyphs differently, so the comparison there is reported, not asserted.
    /// </summary>
    public sealed record Scenario(string Name, Size Size, Func<Control> Create, Rectangle[] TextRectangles);

    /// <summary>The classic default control font, fixed so neither side falls back to a different one.</summary>
    public static readonly Font StandardFont = new("Microsoft Sans Serif", 8.25f);

    /// <summary>
    /// Computed lazily, not by a field initializer: a field initializer runs in the declaration
    /// order Roslyn assigns the partial type's members across every VisualScenarios.*.cs file,
    /// an order this project does not control, so it could run before the Size fields each
    /// category method reads are themselves initialized. Reading <see cref="All"/> instead
    /// triggers the type's whole static constructor first, which leaves every field set no
    /// matter which file declared it, and only then runs this getter's body.
    /// </summary>
    public static IReadOnlyList<Scenario> All => s_all ??= BuildAll();

    private static IReadOnlyList<Scenario>? s_all;

    private static IReadOnlyList<Scenario> BuildAll() =>
    [
        .. ButtonScenarios(),
        .. CheckScenarios(),
        .. StaticScenarios(),
        .. TextScenarios(),
        .. ListScenarios(),
        .. RangeScenarios(),
        .. ContainerScenarios(),
        .. DateScenarios(),
        .. StripScenarios(),
        .. GridScenarios(),
    ];

    /// <summary>
    /// Builds a scenario whose control starts with the standard font, the scenario's fixed size
    /// and dialog colours (the ones a control hosted directly on a form would show), then hands
    /// it to <paramref name="configure"/> for the states and text that make the scenario what it
    /// is. <paramref name="configure"/> may still override the colours where a scenario needs the
    /// control's own default (an edit control's white background, for instance).
    /// </summary>
    private static Scenario Create<TControl>(
        string name,
        Size size,
        Action<TControl> configure,
        Rectangle[]? textRectangles = null)
        where TControl : Control, new()
    {
        return new Scenario(name, size, () =>
        {
            var control = new TControl
            {
                Font = StandardFont,
                Size = size,
                BackColor = SystemColors.Control,
                ForeColor = SystemColors.ControlText,
            };
            configure(control);
            return control;
        }, textRectangles ?? []);
    }

    /// <summary>The whole client area, for a scenario whose only text sits wherever the control paints it.</summary>
    private static Rectangle[] FullArea(Size size) => [new Rectangle(Point.Empty, size)];
}
