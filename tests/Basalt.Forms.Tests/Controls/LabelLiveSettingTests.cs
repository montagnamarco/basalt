using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Label.LiveSetting: a live region announces new text through its AccessibilityObject, once it is anything but Off.</summary>
public class LabelLiveSettingTests
{
    private sealed class SpyAccessibleObject : Control.ControlAccessibleObject
    {
        public int RaiseCount;

        public SpyAccessibleObject(Control owner) : base(owner)
        {
        }

        public override bool RaiseLiveRegionChanged()
        {
            RaiseCount++;
            return true;
        }
    }

    private sealed class SpyLabel : Label
    {
        private readonly SpyAccessibleObject _spy;

        public SpyLabel()
        {
            _spy = new SpyAccessibleObject(this);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => _spy;

        public int RaiseCount => _spy.RaiseCount;
    }

    [Fact]
    public void Label_implements_IAutomationLiveRegion_and_stores_its_LiveSetting()
    {
        var label = new Label();
        IAutomationLiveRegion region = label;

        Assert.Equal(AutomationLiveSetting.Off, region.LiveSetting);

        region.LiveSetting = AutomationLiveSetting.Polite;

        Assert.Equal(AutomationLiveSetting.Polite, label.LiveSetting);
    }

    [Fact]
    public void Changing_text_on_an_Off_label_never_tells_its_AccessibleObject_to_announce()
    {
        var label = new SpyLabel { LiveSetting = AutomationLiveSetting.Off };

        label.Text = "first";

        Assert.Equal(0, label.RaiseCount);
    }

    [Fact]
    public void Changing_text_on_a_live_label_tells_its_AccessibleObject_to_announce()
    {
        var label = new SpyLabel { LiveSetting = AutomationLiveSetting.Polite };

        label.Text = "updated";

        Assert.Equal(1, label.RaiseCount);

        label.Text = "updated again";

        Assert.Equal(2, label.RaiseCount);
    }
}
