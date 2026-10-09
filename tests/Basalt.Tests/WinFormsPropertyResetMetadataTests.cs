using System.ComponentModel;
using System.IO.Ports;
using Basalt.Designer;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsPropertyResetMetadataTests
{
    [Fact]
    public void SerialPortIsTrustedBeyondTheFormsAssembly()
    {
        using var port = new SerialPort();
        var descriptor = TypeDescriptor.GetProperties(port)[nameof(port.BaudRate)]!;

        var state = WinFormsPropertyResetMetadata.Describe(port, descriptor, WinFormsSourceValueState.Evaluated);

        // SerialPort declares no DefaultValueAttribute and no ResetBaudRate method, so a
        // trusted read correctly reports there is nothing to reset to - unlike the
        // untrusted fallback, which always offers Reset regardless of whether it would do
        // anything. Modified is true either way here: the default ReflectPropertyDescriptor
        // ShouldSerializeValue, which SerialPort never overrides, always returns true
        // without a DefaultValueAttribute to compare against.
        Assert.True(state.IsModified);
        Assert.False(state.CanReset);
    }

    [Fact]
    public void AUserControlSubclassingAFormsControlIsTrustedForItsOwnProperties()
    {
        // A "user control" - the shape the designer will host later - is ordinarily a
        // subclass of a Basalt.Forms control, declared in the user's own assembly (here,
        // the test assembly stands in for it). Its own new property, with a
        // DefaultValueAttribute and no overridden hooks, must be read the trusted way:
        // not the untrusted fallback, which always reports Modified and CanReset true
        // regardless of the attribute.
        using var control = new UserControlSubclass();
        var descriptor = TypeDescriptor.GetProperties(control)[nameof(UserControlSubclass.Level)]!;

        var atDefault = WinFormsPropertyResetMetadata.Describe(control, descriptor, WinFormsSourceValueState.Evaluated);
        Assert.False(atDefault.IsModified);
        Assert.False(atDefault.CanReset);

        control.Level = 7;
        var changed = WinFormsPropertyResetMetadata.Describe(control, descriptor, WinFormsSourceValueState.Evaluated);
        Assert.True(changed.IsModified);
        Assert.True(changed.CanReset);
    }

    private sealed class UserControlSubclass : Forms.Control
    {
        [DefaultValue(3)]
        public int Level { get; set; } = 3;
    }

    [Fact]
    public void TrustedUserControlOwnShouldSerializeAndResetHooksRun()
    {
        // Owner decision: a user control's own ShouldSerializeX and ResetX run during designer
        // inspection and reset, the same in-process trust Visual Studio extends to user controls.
        // The counters prove both hooks ran rather than being skipped.
        using var control = new HookedControl { Level = 7 };
        var descriptor = TypeDescriptor.GetProperties(control)[nameof(HookedControl.Level)]!;

        var state = WinFormsPropertyResetMetadata.Describe(control, descriptor, WinFormsSourceValueState.Evaluated);

        Assert.True(control.ShouldSerializeCalls > 0);
        Assert.True(state.IsModified);
        Assert.True(state.CanReset);

        Assert.True(WinFormsPropertyInspector.ResetValue(control, nameof(HookedControl.Level)));
        Assert.Equal(1, control.ResetCalls);
        Assert.Equal(3, control.Level);
    }

    private sealed class HookedControl : Forms.Control
    {
        public int ShouldSerializeCalls { get; private set; }

        public int ResetCalls { get; private set; }

        public int Level { get; set; } = 3;

        public bool ShouldSerializeLevel()
        {
            ShouldSerializeCalls++;
            return Level != 3;
        }

        public void ResetLevel()
        {
            ResetCalls++;
            Level = 3;
        }
    }

    [Fact]
    public void TrustedUserControlWhoseOwnShouldSerializeThrowsIsModifiedAndNotResettable()
    {
        // The reflection descriptor already absorbs an exception from ShouldSerializeX (measured:
        // ShouldSerializeValue returns true), so this pins the designer-visible result. The getter
        // test below is the one that needs Describe's own containment.
        using var control = new ThrowingShouldSerializeControl();
        var descriptor = TypeDescriptor.GetProperties(control)[nameof(ThrowingShouldSerializeControl.Level)]!;

        var state = WinFormsPropertyResetMetadata.Describe(control, descriptor, WinFormsSourceValueState.Evaluated);

        Assert.True(state.IsModified);
        Assert.False(state.CanReset);
    }

    private sealed class ThrowingShouldSerializeControl : Forms.Control
    {
        public int Level { get; set; } = 3;

        public bool ShouldSerializeLevel() => throw new InvalidOperationException("User serialization hook failed.");
    }

    [Fact]
    public void TrustedUserControlWhoseOwnGetterThrowsIsModifiedAndNotResettable()
    {
        // Unlike ShouldSerializeX, an exception from a user property getter escapes the reflection
        // descriptor (measured: GetValue throws TargetInvocationException), so Describe must contain it.
        using var control = new ThrowingGetterControl();
        var descriptor = TypeDescriptor.GetProperties(control)[nameof(ThrowingGetterControl.Level)]!;

        var state = WinFormsPropertyResetMetadata.Describe(control, descriptor, WinFormsSourceValueState.Evaluated);

        Assert.True(state.IsModified);
        Assert.False(state.CanReset);
    }

    private sealed class ThrowingGetterControl : Forms.Control
    {
        public int Level
        {
            get => throw new InvalidOperationException("User getter failed.");
            set { }
        }
    }

    [Fact]
    public void TrustedUserControlWhoseOwnResetThrowsIsReportedAsNotReset()
    {
        using var control = new ThrowingResetControl { Level = 7 };

        Assert.False(WinFormsPropertyInspector.ResetValue(control, nameof(ThrowingResetControl.Level)));
        Assert.Equal(7, control.Level);
    }

    private sealed class ThrowingResetControl : Forms.Control
    {
        public int Level { get; set; } = 3;

        public bool ShouldSerializeLevel() => Level != 3;

        public void ResetLevel() => throw new InvalidOperationException("User reset hook failed.");
    }

    [Fact]
    public void ExceptionFromABuiltInFormsHookPropagatesInsteadOfBeingContained()
    {
        // Containment is for user code. A failing hook on a Forms-declared property is a bug in
        // Basalt's own control, and reporting it as "modified, not resettable" would hide that bug.
        // No Forms type can be made to throw from a test, so the descriptor is a stand-in whose
        // ComponentType is the built-in Button: the component and the declaring type are both Forms.
        using var button = new Forms.Button();
        var descriptor = new FailingDescriptor("Level", typeof(Forms.Button), typeof(int));

        Assert.Throws<InvalidOperationException>(() =>
            WinFormsPropertyResetMetadata.Describe(button, descriptor, WinFormsSourceValueState.Evaluated));
    }

    [Fact]
    public void ExceptionFromAUserSubclassHookIsStillContained()
    {
        // The same failing descriptor declared on a user subclass is user code, so Describe contains it.
        using var control = new ThrowingGetterControl();
        var descriptor = new FailingDescriptor("Level", typeof(ThrowingGetterControl), typeof(int));

        var state = WinFormsPropertyResetMetadata.Describe(control, descriptor, WinFormsSourceValueState.Evaluated);

        Assert.True(state.IsModified);
        Assert.False(state.CanReset);
    }

    /// <summary>A property whose getter and reset hook always throw, declared on the given type.</summary>
    private sealed class FailingDescriptor(string name, Type componentType, Type propertyType)
        : PropertyDescriptor(name, null)
    {
        public override Type ComponentType { get; } = componentType;

        public override Type PropertyType { get; } = propertyType;

        public override bool IsReadOnly => false;

        public override bool CanResetValue(object component) => true;

        public override object? GetValue(object? component) =>
            throw new InvalidOperationException("Hook failed.");

        public override void ResetValue(object component) =>
            throw new InvalidOperationException("Hook failed.");

        public override void SetValue(object? component, object? value) =>
            throw new InvalidOperationException("Hook failed.");

        public override bool ShouldSerializeValue(object component) => true;
    }

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
