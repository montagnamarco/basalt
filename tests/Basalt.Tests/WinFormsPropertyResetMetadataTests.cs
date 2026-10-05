using System.ComponentModel;
using Basalt.Designer;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsPropertyResetMetadataTests
{
    [Fact]
    public void ExplicitBuiltInDefaultIsNotBoldOrResettable()
    {
        using var button = new Forms.Button();
        var properties = TypeDescriptor.GetProperties(button);
        var enabled = properties[nameof(button.Enabled)]!;
        var text = properties[nameof(button.Text)]!;
        Assert.True(button.Enabled);

        var state = WinFormsPropertyResetMetadata.Describe(
            button, enabled, WinFormsSourceValueState.Evaluated);

        Assert.False(state.IsModified);
        Assert.False(state.CanReset);
        var emptyText = WinFormsPropertyResetMetadata.Describe(
            button, text, WinFormsSourceValueState.Evaluated);
        Assert.False(emptyText.IsModified);
        Assert.False(emptyText.CanReset);
    }

    [Fact]
    public void ChangedBuiltInValuesUseDescriptorResetSemantics()
    {
        using var button = new Forms.Button { Enabled = false, Text = "Run" };
        var properties = TypeDescriptor.GetProperties(button);

        var enabled = WinFormsPropertyResetMetadata.Describe(
            button, properties[nameof(button.Enabled)]!, WinFormsSourceValueState.Evaluated);
        var text = WinFormsPropertyResetMetadata.Describe(
            button, properties[nameof(button.Text)]!, WinFormsSourceValueState.Evaluated);

        Assert.True(enabled.IsModified);
        Assert.True(enabled.CanReset);
        Assert.True(text.IsModified);
        Assert.True(text.CanReset);
        Assert.Equal("Run", button.Text);
        Assert.False(button.Enabled);
    }

    [Fact]
    public void UnknownSourceExpressionRemainsBoldAndResettableWhenPreviewShowsTheDefault()
    {
        using var button = new Forms.Button();
        var enabled = TypeDescriptor.GetProperties(button)[nameof(button.Enabled)]!;

        var absent = WinFormsPropertyResetMetadata.Describe(
            button, enabled, WinFormsSourceValueState.Absent);
        var unresolved = WinFormsPropertyResetMetadata.Describe(
            button, enabled, WinFormsSourceValueState.Unresolved);

        Assert.False(absent.IsModified);
        Assert.False(absent.CanReset);
        Assert.True(unresolved.IsModified);
        Assert.True(unresolved.CanReset);
    }

    [Fact]
    public void ApplicationDescriptorHooksNeverRunDuringDesignerMetadataInspection()
    {
        var control = new ApplicationControl();
        var descriptor = TypeDescriptor.GetProperties(control)[nameof(control.Value)]!;

        var state = WinFormsPropertyResetMetadata.Describe(
            control, descriptor, WinFormsSourceValueState.Evaluated);

        Assert.True(state.IsModified);
        Assert.True(state.CanReset);
        Assert.Equal(0, control.Gets);
        Assert.Equal(0, control.SerializationCalls);
        Assert.Equal(0, control.ResetCalls);
    }

    private sealed class ApplicationControl
    {
        public int Gets { get; private set; }

        public int SerializationCalls { get; private set; }

        public int ResetCalls { get; private set; }

        public string Value
        {
            get
            {
                Gets++;
                throw new InvalidOperationException("Application property getter must not run.");
            }
            set => throw new InvalidOperationException("Application property setter must not run.");
        }

        public bool ShouldSerializeValue()
        {
            SerializationCalls++;
            throw new InvalidOperationException("Application serialization hook must not run.");
        }

        public void ResetValue()
        {
            ResetCalls++;
            throw new InvalidOperationException("Application reset hook must not run.");
        }
    }
}
