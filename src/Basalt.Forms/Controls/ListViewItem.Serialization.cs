using System.Drawing;
using System.Runtime.Serialization;

namespace System.Windows.Forms;

public partial class ListViewItem
{
    protected ListViewItem(SerializationInfo info, StreamingContext context) : this()
    {
        Deserialize(info, context);
    }

    protected virtual void Deserialize(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        int? imageIndex = null;
        string? imageKey = null;
        var subItemCount = 0;

        foreach (SerializationEntry entry in info)
        {
            switch (entry.Name)
            {
                case nameof(Text): Text = info.GetString(entry.Name) ?? string.Empty; break;
                case nameof(ImageIndex): imageIndex = info.GetInt32(entry.Name); break;
                case nameof(ImageKey): imageKey = info.GetString(entry.Name); break;
                case "SubItemCount": subItemCount = info.GetInt32(entry.Name); break;
                case nameof(BackColor): BackColor = (Color)info.GetValue(entry.Name, typeof(Color))!; break;
                case nameof(Checked): Checked = info.GetBoolean(entry.Name); break;
                case nameof(Font): _subItems[0].Font = (Font?)info.GetValue(entry.Name, typeof(Font)); break;
                case nameof(ForeColor): ForeColor = (Color)info.GetValue(entry.Name, typeof(Color))!; break;
                case nameof(UseItemStyleForSubItems): UseItemStyleForSubItems = info.GetBoolean(entry.Name); break;
                case nameof(Group): Group = (ListViewGroup?)info.GetValue(entry.Name, typeof(ListViewGroup)); break;
                case nameof(Name): Name = info.GetString(entry.Name) ?? string.Empty; break;
                case nameof(ToolTipText): ToolTipText = info.GetString(entry.Name) ?? string.Empty; break;
                case nameof(StateImageIndex): StateImageIndex = info.GetInt32(entry.Name); break;
            }
        }

        if (!string.IsNullOrEmpty(imageKey)) ImageKey = imageKey;
        else if (imageIndex.HasValue) ImageIndex = imageIndex.Value;

        for (var i = 1; i < subItemCount; i++)
        {
            var source = (ListViewSubItem)info.GetValue($"SubItem{i}", typeof(ListViewSubItem))!;
            _subItems.Add(source.CloneFor(this));
        }
    }

    protected virtual void Serialize(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue(nameof(Text), Text);
        info.AddValue(nameof(ImageIndex), ImageIndex);
        if (!string.IsNullOrEmpty(ImageKey)) info.AddValue(nameof(ImageKey), ImageKey);
        if (_subItems.Count > 1)
        {
            info.AddValue("SubItemCount", _subItems.Count);
            for (var i = 1; i < _subItems.Count; i++)
                info.AddValue($"SubItem{i}", _subItems[i], typeof(ListViewSubItem));
        }

        info.AddValue(nameof(BackColor), BackColor);
        info.AddValue(nameof(Checked), Checked);
        info.AddValue(nameof(Font), _subItems[0].Font, typeof(Font));
        info.AddValue(nameof(ForeColor), ForeColor);
        info.AddValue(nameof(UseItemStyleForSubItems), UseItemStyleForSubItems);
        if (Group is not null) info.AddValue(nameof(Group), Group, typeof(ListViewGroup));
        if (Name.Length > 0) info.AddValue(nameof(Name), Name);
        if (ToolTipText.Length > 0) info.AddValue(nameof(ToolTipText), ToolTipText);
        if (StateImageIndex >= 0) info.AddValue(nameof(StateImageIndex), StateImageIndex);
    }

    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) => Serialize(info, context);
}
