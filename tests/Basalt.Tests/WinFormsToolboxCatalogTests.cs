using System.ComponentModel;
using Basalt.Designer.Toolbox;

namespace Basalt.Tests;

public class WinFormsToolboxCatalogTests
{
    [Fact]
    public void DiscoversConcretePublicControlsAndHonorsToolboxVisibility()
    {
        var items = WinFormsToolboxCatalog.Describe(typeof(WinFormsToolboxCatalogTests).Assembly, typeof(TestControl));
        Assert.Equal([typeof(AvailableControl)], items.Select(item => item.ControlType));
        Assert.Equal(nameof(AvailableControl), Assert.Single(items).DisplayName);
    }

    public class TestControl;
    public sealed class AvailableControl : TestControl;
    [ToolboxItem(false)]
    public sealed class HiddenControl : TestControl;
    public abstract class AbstractControl : TestControl;
    public sealed class GenericControl<T> : TestControl;
    public sealed class RequiresArgumentControl(string text) : TestControl
    {
        public string Text { get; } = text;
    }
}
