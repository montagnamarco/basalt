namespace System.Windows.Forms;

public partial class Form
{
    public new class ControlCollection : Control.ControlCollection
    {
        public ControlCollection(Form owner) : base(owner)
        {
        }

        public override void Add(Control? value) => base.Add(value);
        public override void Remove(Control? value) => base.Remove(value);
    }
}
