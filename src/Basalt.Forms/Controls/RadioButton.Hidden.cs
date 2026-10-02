using System.ComponentModel;

namespace System.Windows.Forms;

public partial class RadioButton
{
    protected override AccessibleObject CreateAccessibilityInstance() => new RadioButtonAccessibleObject(this);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassName = "Button";
            return parameters;
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DoubleClick
    {
        add => base.DoubleClick += value;
        remove => base.DoubleClick -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event MouseEventHandler? MouseDoubleClick
    {
        add => base.MouseDoubleClick += value;
        remove => base.MouseDoubleClick -= value;
    }

    /// <summary>What a RadioButton's AccessibilityObject answers: RadioButton role, Checked state, and selecting it as the default action (ButtonBaseAccessibleObject's Click would toggle, not select-and-stick).</summary>
    public class RadioButtonAccessibleObject : ControlAccessibleObject
    {
        public RadioButtonAccessibleObject(RadioButton owner) : base(owner)
        {
        }

        public override string? DefaultAction => "Select";

        public override void DoDefaultAction()
        {
            if (Owner is RadioButton { Enabled: true } radioButton) radioButton.OnClick(EventArgs.Empty);
        }

        public override string? KeyboardShortcut => base.KeyboardShortcut;

        public override string? Name => base.Name;

        public override AccessibleRole Role => AccessibleRole.RadioButton;

        public override AccessibleStates State
        {
            get
            {
                var state = base.State;
                if (Owner is RadioButton { Checked: true }) state |= AccessibleStates.Checked;

                return state;
            }
        }
    }
}
