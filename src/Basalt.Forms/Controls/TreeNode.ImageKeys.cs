using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A node's pictures and font by string key instead of by index, its state
/// image (for CheckBoxes-less custom check marks through StateImageList),
/// and the tooltip shown over it.
/// </summary>
public partial class TreeNode
{
    private string _imageKey = string.Empty;
    private string _selectedImageKey = string.Empty;
    private int _stateImageIndex = -1;
    private string _stateImageKey = string.Empty;
    private Font? _nodeFont;
    private string _toolTipText = string.Empty;

    [DefaultValue("")]
    public string ImageKey
    {
        get => _imageKey;
        set
        {
            value ??= string.Empty;
            if (_imageKey == value) return;

            _imageKey = value;
            TreeView?.Invalidate();
        }
    }

    [DefaultValue(null)]
    public Font? NodeFont
    {
        get => _nodeFont;
        set
        {
            if (_nodeFont == value) return;

            _nodeFont = value;
            TreeView?.Invalidate();
        }
    }

    [DefaultValue("")]
    public string SelectedImageKey
    {
        get => _selectedImageKey;
        set
        {
            value ??= string.Empty;
            if (_selectedImageKey == value) return;

            _selectedImageKey = value;
            TreeView?.Invalidate();
        }
    }

    [DefaultValue(-1)]
    public int StateImageIndex
    {
        get => _stateImageIndex;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);
            if (_stateImageIndex == value) return;

            _stateImageIndex = value;
            TreeView?.Invalidate();
        }
    }

    [DefaultValue("")]
    public string StateImageKey
    {
        get => _stateImageKey;
        set
        {
            value ??= string.Empty;
            if (_stateImageKey == value) return;

            _stateImageKey = value;
            TreeView?.Invalidate();
        }
    }

    [DefaultValue("")]
    public string ToolTipText
    {
        get => _toolTipText;
        set => _toolTipText = value ?? string.Empty;
    }

    /// <summary>The node's own picture index: ImageKey, when set, looked up in the list; else ImageIndex as stored.</summary>
    internal int ResolveImageIndex(ImageList? list) =>
        _imageKey.Length > 0 ? list?.Images.IndexOfKey(_imageKey) ?? -1 : _imageIndex;

    /// <summary>The node's selected-state picture index, resolved the same way as <see cref="ResolveImageIndex"/>.</summary>
    internal int ResolveSelectedImageIndex(ImageList? list) =>
        _selectedImageKey.Length > 0 ? list?.Images.IndexOfKey(_selectedImageKey) ?? -1 : _selectedImageIndex;

    /// <summary>The node's state-image index (the check mark picture), resolved the same way.</summary>
    internal int ResolveStateImageIndex(ImageList? list) =>
        _stateImageKey.Length > 0 ? list?.Images.IndexOfKey(_stateImageKey) ?? -1 : _stateImageIndex;
}
