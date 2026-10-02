namespace System.Windows.Forms;

public abstract partial class ButtonBase
{
    /// <summary>What a Button, CheckBox or RadioButton's AccessibilityObject answers beyond the generic ControlAccessibleObject: its own mnemonic, its pressed state, and clicking it as the default action.</summary>
    public class ButtonBaseAccessibleObject : ControlAccessibleObject
    {
        public ButtonBaseAccessibleObject(Control owner) : base(owner)
        {
        }

        /// <summary>The default action of any button-like control: clicking it, exactly as Space or a mouse click would.</summary>
        public override void DoDefaultAction()
        {
            if (Owner is ButtonBase { Enabled: true } button) button.OnClick(EventArgs.Empty);
        }

        /// <summary>The same Alt+mnemonic logic ControlAccessibleObject already applies to a button's own Text.</summary>
        public override string? KeyboardShortcut => base.KeyboardShortcut;

        public override string? Name => base.Name;

        public override AccessibleStates State
        {
            get
            {
                var state = base.State;

                if (Owner is ButtonBase button)
                {
                    if (button.IsDefault) state |= AccessibleStates.Default;
                    if (button.Pushed) state |= AccessibleStates.Pressed;
                }

                return state;
            }
        }
    }
}
