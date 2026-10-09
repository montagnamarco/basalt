using System.ComponentModel;
using Basalt.Designer;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

public class WinFormsExpandableNullChildTests
{
    [Fact]
    public void NullReferenceMemberKeepsItsRowWithNoValue()
    {
        // The application's expandable value has a string? member that is null. Before the fix
        // the generic children list skipped it, so the user could not see or set that member.
        var component = new NullableMemberHost
        {
            Options = new NullableOptions { Label = null, Count = 2 },
        };

        var children = WinFormsExpandableProperties.Children(
            component, nameof(NullableMemberHost.Options), isExplicit: true);

        var label = Assert.Single(children, child => child.Name == nameof(NullableOptions.Label));
        Assert.Null(label.CurrentValue);
        Assert.Null(label.DefaultValue);
        Assert.Equal(nameof(NullableMemberHost.Options), label.ParentName);
        Assert.Equal(1, label.Depth);
        // A null member has no value to be "set" to, so it is not set even though its parent is.
        Assert.False(label.WasSet);

        var count = Assert.Single(children, child => child.Name == nameof(NullableOptions.Count));
        Assert.Equal("2", count.CurrentValue);
    }

    [Fact]
    public void NullChildOfAnExplicitParentIsNotSetAndOffersNoReset()
    {
        // Reset of a child goes through SetNestedPropertyAsync, which throws "The child has no default
        // value." when the default instance's child is null too. Offering Reset for a null child would
        // therefore offer a command that always fails, so the row must report neither IsSet nor CanReset.
        var component = new NullableMemberHost
        {
            Options = new NullableOptions { Label = null, Count = 2 },
        };

        var children = WinFormsExpandableProperties.Children(
            component, nameof(NullableMemberHost.Options), isExplicit: true);

        var label = Assert.Single(children, child => child.Name == nameof(NullableOptions.Label));
        Assert.False(label.IsSet);
        Assert.False(label.CanReset);

        // The non-null sibling of the same explicit parent is still set. Its CanReset stays false here
        // only because this converter has no CreateInstance, which makes every child read-only.
        var count = Assert.Single(children, child => child.Name == nameof(NullableOptions.Count));
        Assert.True(count.IsSet);
    }

    [Fact]
    public void NonNullReferenceMemberStillShowsItsText()
    {
        var component = new NullableMemberHost
        {
            Options = new NullableOptions { Label = "Ready", Count = 5 },
        };

        var children = WinFormsExpandableProperties.Children(
            component, nameof(NullableMemberHost.Options), isExplicit: true);

        var label = Assert.Single(children, child => child.Name == nameof(NullableOptions.Label));
        Assert.Equal("Ready", label.CurrentValue);
        Assert.Equal("Ready", label.DefaultValue);
    }

    public sealed class NullableMemberHost
    {
        public NullableOptions Options { get; set; } = new();
    }

    [TypeConverter(typeof(NullableOptionsConverter))]
    public sealed class NullableOptions
    {
        public string? Label { get; set; }

        public int Count { get; set; } = 1;
    }

    /// <summary>Expandable like any application converter that lists its own members.</summary>
    public sealed class NullableOptionsConverter : ExpandableObjectConverter
    {
    }
}
