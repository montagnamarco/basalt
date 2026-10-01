using System.Collections.ObjectModel;

namespace System.Windows.Forms;

/// <summary>A folder offered on the side of a file dialog, by path or by known-folder identifier.</summary>
public class FileDialogCustomPlace
{
    private string _path = string.Empty;
    private Guid _knownFolderGuid = Guid.Empty;

    public FileDialogCustomPlace(string? path)
    {
        Path = path!;
    }

    public FileDialogCustomPlace(Guid knownFolderGuid)
    {
        KnownFolderGuid = knownFolderGuid;
    }

    /// <summary>The folder's path; setting it forgets the known folder, as Windows' does.</summary>
    public string Path
    {
        get => _path;
        set
        {
            _path = value ?? string.Empty;
            _knownFolderGuid = Guid.Empty;
        }
    }

    /// <summary>The known folder's identifier; setting it forgets the path.</summary>
    public Guid KnownFolderGuid
    {
        get => _knownFolderGuid;
        set
        {
            _path = string.Empty;
            _knownFolderGuid = value;
        }
    }

    public override string ToString() => $"{GetType()} Path: {Path} KnownFolderGuid: {KnownFolderGuid}";
}

public class FileDialogCustomPlacesCollection : Collection<FileDialogCustomPlace>
{
    public void Add(string path) => Add(new FileDialogCustomPlace(path));

    public void Add(Guid knownFolderGuid) => Add(new FileDialogCustomPlace(knownFolderGuid));
}
