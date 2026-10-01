using System.Windows.Forms.VisualStyles;

namespace Basalt.Forms.Tests.VisualStyles;

/// <summary>
/// VisualStyleElement.CreateElement and the whole nested class/part/state
/// tree it exposes, tested the way it would fail if a class, part or state
/// number were missing or swapped: by reading the triplet back.
/// </summary>
public class VisualStyleElementTests
{
    [Fact]
    public void CreateElement_refuses_a_null_class_name()
    {
        Assert.Throws<ArgumentNullException>(() => VisualStyleElement.CreateElement(null!, 1, 1));
    }

    [Fact]
    public void CreateElement_carries_the_exact_class_part_and_state_given()
    {
        var element = VisualStyleElement.CreateElement("BUTTON", 1, 3);

        Assert.Equal("BUTTON", element.ClassName);
        Assert.Equal(1, element.Part);
        Assert.Equal(3, element.State);
    }

    [Fact]
    public void PushButton_states_are_the_button_class_at_part_one()
    {
        Assert.Equal(("BUTTON", 1, 1), Triplet(VisualStyleElement.Button.PushButton.Normal));
        Assert.Equal(("BUTTON", 1, 2), Triplet(VisualStyleElement.Button.PushButton.Hot));
        Assert.Equal(("BUTTON", 1, 3), Triplet(VisualStyleElement.Button.PushButton.Pressed));
        Assert.Equal(("BUTTON", 1, 4), Triplet(VisualStyleElement.Button.PushButton.Disabled));
        Assert.Equal(("BUTTON", 1, 5), Triplet(VisualStyleElement.Button.PushButton.Default));
    }

    [Fact]
    public void CheckBox_mixed_states_are_the_button_class_at_part_three()
    {
        Assert.Equal(("BUTTON", 3, 9), Triplet(VisualStyleElement.Button.CheckBox.MixedNormal));
        Assert.Equal(("BUTTON", 3, 12), Triplet(VisualStyleElement.Button.CheckBox.MixedDisabled));
    }

    [Fact]
    public void RadioButton_and_CheckBox_share_the_eight_checked_unchecked_states()
    {
        Assert.Equal(("BUTTON", 2, 5), Triplet(VisualStyleElement.Button.RadioButton.CheckedNormal));
        Assert.Equal(("BUTTON", 3, 5), Triplet(VisualStyleElement.Button.CheckBox.CheckedNormal));
    }

    [Fact]
    public void Each_property_call_returns_an_independent_but_equal_triplet()
    {
        var first = VisualStyleElement.Button.PushButton.Normal;
        var second = VisualStyleElement.Button.PushButton.Normal;

        Assert.NotSame(first, second);
        Assert.Equal(Triplet(first), Triplet(second));
    }

    [Fact]
    public void ScrollBar_arrow_states_match_the_published_ScrollBarArrowButtonState_values()
    {
        // The arrow button part packs four directions times four states into
        // 16 consecutive state numbers; this is the public contract other
        // code relies on when it casts a ScrollBarArrowButtonState to int.
        Assert.Equal(1, (int)ScrollBarArrowButtonState.UpNormal);
        Assert.Equal(("SCROLLBAR", 1, 1), Triplet(VisualStyleElement.ScrollBar.ArrowButton.UpNormal));
        Assert.Equal(16, (int)ScrollBarArrowButtonState.RightDisabled);
        Assert.Equal(("SCROLLBAR", 1, 16), Triplet(VisualStyleElement.ScrollBar.ArrowButton.RightDisabled));
    }

    [Fact]
    public void TrackBar_thumb_states_match_the_published_TrackBarThumbState_values()
    {
        Assert.Equal(("TRACKBAR", 3, 1), Triplet(VisualStyleElement.TrackBar.Thumb.Normal));
        Assert.Equal(("TRACKBAR", 3, 5), Triplet(VisualStyleElement.TrackBar.Thumb.Disabled));
    }

    [Fact]
    public void Window_caption_active_inactive_disabled_are_three_distinct_states()
    {
        var active = Triplet(VisualStyleElement.Window.Caption.Active);
        var inactive = Triplet(VisualStyleElement.Window.Caption.Inactive);
        var disabled = Triplet(VisualStyleElement.Window.Caption.Disabled);

        Assert.Equal("WINDOW", active.Item1);
        Assert.Equal(active.Item2, inactive.Item2);
        Assert.Equal(active.Item2, disabled.Item2);
        Assert.NotEqual(active.Item3, inactive.Item3);
        Assert.NotEqual(active.Item3, disabled.Item3);
        Assert.NotEqual(inactive.Item3, disabled.Item3);
    }

    private static (string, int, int) Triplet(VisualStyleElement element) => (element.ClassName, element.Part, element.State);
}
