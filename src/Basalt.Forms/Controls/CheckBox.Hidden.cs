namespace System.Windows.Forms;

public partial class CheckBox
{
    protected override AccessibleObject CreateAccessibilityInstance() => new CheckBoxAccessibleObject(this);

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

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public new event EventHandler? DoubleClick
    {
        add => base.DoubleClick += value;
        remove => base.DoubleClick -= value;
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public new event MouseEventHandler? MouseDoubleClick
    {
        add => base.MouseDoubleClick += value;
        remove => base.MouseDoubleClick -= value;
    }

    /// <summary>What a CheckBox's AccessibilityObject answers beyond ButtonBaseAccessibleObject: toggling the box as its default action, and the Checked/Indeterminate state.</summary>
    public class CheckBoxAccessibleObject : ButtonBaseAccessibleObject
    {
        public CheckBoxAccessibleObject(Control owner) : base(owner)
        {
        }

        public override string? DefaultAction => Owner is CheckBox { Checked: true } ? "Uncheck" : "Check";

        public override void DoDefaultAction()
        {
            if (Owner is CheckBox { Enabled: true } checkBox) checkBox.OnClick(EventArgs.Empty);
        }

        public override AccessibleRole Role => AccessibleRole.CheckButton;

        public override AccessibleStates State
        {
            get
            {
                var state = base.State;

                if (Owner is CheckBox checkBox)
                {
                    state |= checkBox.CheckState switch
                    {
                        CheckState.Checked => AccessibleStates.Checked,
                        CheckState.Indeterminate => AccessibleStates.Indeterminate,
                        _ => AccessibleStates.None,
                    };
                }

                return state;
            }
        }
    }
}
