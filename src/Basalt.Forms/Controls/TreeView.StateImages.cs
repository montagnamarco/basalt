using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// The tree view's own default picture by string key (ImageKey,
/// SelectedImageKey), the separate list CheckBoxes-less state pictures
/// come from (StateImageList), and whether a node's label gets a tooltip.
/// </summary>
public partial class TreeView
{
    private string _imageKey = string.Empty;
    private string _selectedImageKey = string.Empty;
    private ImageList? _stateImageList;
    private bool _showNodeToolTips;

    [DefaultValue("")]
    [TypeConverter(typeof(TreeViewImageKeyConverter))]
    [RelatedImageList(nameof(ImageList))]
    public string ImageKey
    {
        get => _imageKey;
        set
        {
            value ??= string.Empty;
            if (_imageKey == value) return;

            _imageKey = value;
            Invalidate();
        }
    }

    [DefaultValue("")]
    [TypeConverter(typeof(TreeViewImageKeyConverter))]
    [RelatedImageList(nameof(ImageList))]
    public string SelectedImageKey
    {
        get => _selectedImageKey;
        set
        {
            value ??= string.Empty;
            if (_selectedImageKey == value) return;

            _selectedImageKey = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool ShowNodeToolTips
    {
        get => _showNodeToolTips;
        set => _showNodeToolTips = value;
    }

    [DefaultValue(null)]
    public ImageList? StateImageList
    {
        get => _stateImageList;
        set
        {
            if (_stateImageList == value) return;

            _stateImageList = value;
            Invalidate();
        }
    }

    /// <summary>The tree view's own default picture: ImageKey, when set, looked up in ImageList; else ImageIndex.</summary>
    private int ResolveImageIndex() => _imageKey.Length > 0 ? _imageList?.Images.IndexOfKey(_imageKey) ?? -1 : _imageIndex;

    /// <summary>The tree view's own default selected-state picture, resolved the same way.</summary>
    private int ResolveSelectedImageIndex() => _selectedImageKey.Length > 0 ? _imageList?.Images.IndexOfKey(_selectedImageKey) ?? -1 : _selectedImageIndex;
}
