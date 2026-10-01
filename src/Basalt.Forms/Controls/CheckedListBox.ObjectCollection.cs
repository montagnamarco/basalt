using System.ComponentModel;

namespace System.Windows.Forms;

public partial class CheckedListBox
{
    /// <summary>
    /// The items a CheckedListBox shows, with two extra overloads of Add that
    /// give a new item its initial check state without raising ItemCheck -
    /// that event is for a check actually changing, not one being seeded.
    /// </summary>
    public new class ObjectCollection : ListBox.ObjectCollection
    {
        private readonly CheckedListBox _owner;

        public ObjectCollection(CheckedListBox owner)
            : base(owner)
        {
            _owner = owner;
        }

        public int Add(object item, bool isChecked) => Add(item, isChecked ? CheckState.Checked : CheckState.Unchecked);

        public int Add(object item, CheckState check)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!Enum.IsDefined(check)) throw new InvalidEnumArgumentException(nameof(check), (int)check, typeof(CheckState));

            var index = Add(item);
            _owner.SetInitialCheckState(index, check);

            return index;
        }
    }
}
