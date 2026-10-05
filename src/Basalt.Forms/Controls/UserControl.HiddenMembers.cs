namespace System.Windows.Forms;

public partial class UserControl
{
    public override AutoValidate AutoValidate { get => base.AutoValidate; set => base.AutoValidate = value; }

    public override bool ValidateChildren() => base.ValidateChildren();

    public override bool ValidateChildren(ValidationConstraints validationConstraints) => base.ValidateChildren(validationConstraints);

    public new event EventHandler? AutoSizeChanged { add => base.AutoSizeChanged += value; remove => base.AutoSizeChanged -= value; }

    public new event EventHandler? AutoValidateChanged { add => base.AutoValidateChanged += value; remove => base.AutoValidateChanged -= value; }

    public new event EventHandler? TextChanged { add => base.TextChanged += value; remove => base.TextChanged -= value; }
}
