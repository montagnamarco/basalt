using System.Drawing;
using System.Runtime.Serialization;

namespace System.Windows.Forms;

public partial class TreeNode
{
    protected TreeNode(SerializationInfo info, StreamingContext context) : this()
    {
        Deserialize(info, context);
    }

    protected virtual void Deserialize(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        var childCount = 0;
        int? imageIndex = null;
        int? selectedImageIndex = null;
        int? stateImageIndex = null;
        string? imageKey = null;
        string? selectedImageKey = null;
        string? stateImageKey = null;

        foreach (SerializationEntry entry in info)
        {
            switch (entry.Name)
            {
                case nameof(Text): Text = info.GetString(entry.Name) ?? string.Empty; break;
                case nameof(Name): Name = info.GetString(entry.Name) ?? string.Empty; break;
                case nameof(ToolTipText): ToolTipText = info.GetString(entry.Name) ?? string.Empty; break;
                case "IsChecked": CheckedCore = info.GetBoolean(entry.Name); break;
                case nameof(ImageIndex): imageIndex = info.GetInt32(entry.Name); break;
                case nameof(ImageKey): imageKey = info.GetString(entry.Name); break;
                case nameof(SelectedImageIndex): selectedImageIndex = info.GetInt32(entry.Name); break;
                case nameof(SelectedImageKey): selectedImageKey = info.GetString(entry.Name); break;
                case nameof(StateImageIndex): stateImageIndex = info.GetInt32(entry.Name); break;
                case nameof(StateImageKey): stateImageKey = info.GetString(entry.Name); break;
                case "ChildCount": childCount = info.GetInt32(entry.Name); break;
                case "UserData": Tag = entry.Value; break;
                case nameof(BackColor): BackColor = (Color)info.GetValue(entry.Name, typeof(Color))!; break;
                case nameof(ForeColor): ForeColor = (Color)info.GetValue(entry.Name, typeof(Color))!; break;
                case nameof(NodeFont): NodeFont = (Font?)info.GetValue(entry.Name, typeof(Font)); break;
            }
        }

        if (!string.IsNullOrEmpty(imageKey)) ImageKey = imageKey;
        else if (imageIndex.HasValue) ImageIndex = imageIndex.Value;
        if (!string.IsNullOrEmpty(selectedImageKey)) SelectedImageKey = selectedImageKey;
        else if (selectedImageIndex.HasValue) SelectedImageIndex = selectedImageIndex.Value;
        if (!string.IsNullOrEmpty(stateImageKey)) StateImageKey = stateImageKey;
        else if (stateImageIndex.HasValue) StateImageIndex = stateImageIndex.Value;

        for (var i = 0; i < childCount; i++)
        {
            var child = (TreeNode)info.GetValue($"children{i}", typeof(TreeNode))!;
            Nodes.Add(child.Owner is null ? child : (TreeNode)child.Clone());
        }
    }

    protected virtual void Serialize(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue(nameof(Text), Text);
        info.AddValue(nameof(Name), Name);
        info.AddValue(nameof(ToolTipText), ToolTipText);
        info.AddValue("IsChecked", Checked);
        info.AddValue(nameof(ImageIndex), ImageIndex);
        info.AddValue(nameof(ImageKey), ImageKey);
        info.AddValue(nameof(SelectedImageIndex), SelectedImageIndex);
        info.AddValue(nameof(SelectedImageKey), SelectedImageKey);
        info.AddValue(nameof(StateImageIndex), StateImageIndex);
        info.AddValue(nameof(StateImageKey), StateImageKey);
        info.AddValue(nameof(BackColor), BackColor);
        info.AddValue(nameof(ForeColor), ForeColor);
        info.AddValue(nameof(NodeFont), NodeFont, typeof(Font));
        info.AddValue("ChildCount", Nodes.Count);
        for (var i = 0; i < Nodes.Count; i++) info.AddValue($"children{i}", Nodes[i], typeof(TreeNode));
        if (Tag is not null) info.AddValue("UserData", Tag);
    }

    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) => Serialize(info, context);
}
