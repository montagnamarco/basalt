using Microsoft.VisualBasic.FileIO;

namespace Microsoft.VisualBasic.MyServices;

/// <summary>
/// The well-known folders FileSystemProxy.SpecialDirectories exposes to a VB
/// application, each a thin forward to the framework's own static
/// Microsoft.VisualBasic.FileIO.SpecialDirectories.
/// </summary>
public class SpecialDirectoriesProxy
{
    /// <summary>Created by My.Computer, not by applications: Microsoft's constructor is not public.</summary>
    internal SpecialDirectoriesProxy()
    {
    }

    public string AllUsersApplicationData => SpecialDirectories.AllUsersApplicationData;

    public string CurrentUserApplicationData => SpecialDirectories.CurrentUserApplicationData;

    public string Desktop => SpecialDirectories.Desktop;

    public string MyDocuments => SpecialDirectories.MyDocuments;

    public string MyMusic => SpecialDirectories.MyMusic;

    public string MyPictures => SpecialDirectories.MyPictures;

    public string ProgramFiles => SpecialDirectories.ProgramFiles;

    public string Programs => SpecialDirectories.Programs;

    public string Temp => SpecialDirectories.Temp;
}
