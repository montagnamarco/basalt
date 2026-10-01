using System.Windows.Forms.VisualStyles;

namespace Basalt.Forms.Tests.VisualStyles;

/// <summary>
/// Basalt always draws the classic Windows 2000 look, so no visual style is
/// ever active: VisualStyleInformation must report "nothing is themed", and
/// VisualStyleRenderer must refuse to be constructed, the same failure mode
/// WinForms itself has when the operating system has visual styles turned off.
/// </summary>
public class VisualStyleInformationAndRendererTests
{
    [Fact]
    public void VisualStyleInformation_reports_no_style_is_supported_or_enabled()
    {
        Assert.False(VisualStyleInformation.IsSupportedByOS);
        Assert.False(VisualStyleInformation.IsEnabledByUser);
        Assert.False(VisualStyleInformation.SupportsFlatMenus);
        Assert.Equal(0, VisualStyleInformation.MinimumColorDepth);
        Assert.Equal(string.Empty, VisualStyleInformation.DisplayName);
        Assert.Equal(string.Empty, VisualStyleInformation.ColorScheme);
    }

    [Fact]
    public void VisualStyleRenderer_IsSupported_is_false()
    {
        Assert.False(VisualStyleRenderer.IsSupported);
    }

    [Fact]
    public void VisualStyleRenderer_constructor_from_an_element_throws_when_unsupported()
    {
        var element = VisualStyleElement.Button.PushButton.Normal;

        Assert.Throws<InvalidOperationException>(() => new VisualStyleRenderer(element));
    }

    [Fact]
    public void VisualStyleRenderer_constructor_from_class_part_state_throws_when_unsupported()
    {
        Assert.Throws<InvalidOperationException>(() => new VisualStyleRenderer("BUTTON", 1, 1));
    }

    [Fact]
    public void VisualStyleRenderer_constructor_still_validates_its_arguments_first()
    {
        Assert.Throws<ArgumentNullException>(() => new VisualStyleRenderer((VisualStyleElement)null!));
        Assert.Throws<ArgumentNullException>(() => new VisualStyleRenderer(null!, 1, 1));
    }

    [Fact]
    public void IsElementDefined_is_always_false_because_no_style_is_ever_loaded()
    {
        Assert.False(VisualStyleRenderer.IsElementDefined(VisualStyleElement.Button.PushButton.Normal));
    }
}
