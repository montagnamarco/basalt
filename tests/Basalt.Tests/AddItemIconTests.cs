using Avalonia.Media;
using Basalt.Designer;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The icons Add New Item shows, as colours: each is at least 3:1 against a
/// white list and against the dark theme's, which is what a graphic needs to be
/// seen (WCAG 1.4.11). The pixels the dialog really draws are read in
/// <see cref="AddItemDialogTests"/>.
/// </summary>
public sealed class AddItemIconTests
{
    private static readonly Color Light = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color Dark = Color.FromRgb(0x1F, 0x1F, 0x1F);

    [Fact]
    public void EveryTemplateIconIsAtLeastThreeToOneAgainstBothLists()
    {
        foreach (var template in ItemTemplates.All)
        {
            var kind = Enum.Parse<IconKind>(template.Icon);

            // What IconView draws: the kind's own colour, or grey where it has none.
            var colour = IdeIcons.AccentFor(kind) ?? Colors.Gray;

            Assert.True(AddItemDialogTests.Contrast(colour, Dark) >= 3,
                $"{template.Id} ({kind}) is {colour}: {AddItemDialogTests.Contrast(colour, Dark):0.0}:1 on the dark list");
            Assert.True(AddItemDialogTests.Contrast(colour, Light) >= 3,
                $"{template.Id} ({kind}) is {colour}: {AddItemDialogTests.Contrast(colour, Light):0.0}:1 on the light list");
        }
    }
}
