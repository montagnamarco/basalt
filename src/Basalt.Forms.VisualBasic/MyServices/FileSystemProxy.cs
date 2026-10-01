using System.Collections.ObjectModel;
using System.Text;
using Microsoft.VisualBasic.FileIO;
// The enclosing namespace Microsoft.VisualBasic already declares a (legacy,
// VB6-era) public type named FileSystem: a namespace member always wins over
// a using directive, alias included, so plain "FileSystem" here would bind to
// that one instead of Microsoft.VisualBasic.FileIO.FileSystem. A differently
// named alias sidesteps the clash. System.IO also declares a SearchOption,
// which this plain alias resolves unambiguously.
using IOFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using SearchOption = Microsoft.VisualBasic.FileIO.SearchOption;

namespace Microsoft.VisualBasic.MyServices;

/// <summary>
/// My.Computer.FileSystem: an instance-shaped forward to the framework's own
/// static Microsoft.VisualBasic.FileIO.FileSystem, which already does every
/// bit of the work (the UI/cancel options included) — this proxy exists only
/// because VB's My.Computer.FileSystem is an instance member, not a static one.
/// </summary>
public class FileSystemProxy
{
    /// <summary>Created by My.Computer, not by applications: Microsoft's constructor is not public.</summary>
    internal FileSystemProxy()
    {
    }

    public string CurrentDirectory
    {
        get => IOFileSystem.CurrentDirectory;
        set => IOFileSystem.CurrentDirectory = value;
    }

    public ReadOnlyCollection<DriveInfo> Drives => IOFileSystem.Drives;

    public SpecialDirectoriesProxy SpecialDirectories { get; } = new();

    public string CombinePath(string baseDirectory, string relativePath) => IOFileSystem.CombinePath(baseDirectory, relativePath);

    public void CopyDirectory(string sourceDirectoryName, string destinationDirectoryName) =>
        IOFileSystem.CopyDirectory(sourceDirectoryName, destinationDirectoryName);

    public void CopyDirectory(string sourceDirectoryName, string destinationDirectoryName, bool overwrite) =>
        IOFileSystem.CopyDirectory(sourceDirectoryName, destinationDirectoryName, overwrite);

    public void CopyDirectory(string sourceDirectoryName, string destinationDirectoryName, UIOption showUI) =>
        IOFileSystem.CopyDirectory(sourceDirectoryName, destinationDirectoryName, showUI);

    public void CopyDirectory(string sourceDirectoryName, string destinationDirectoryName, UIOption showUI, UICancelOption onUserCancel) =>
        IOFileSystem.CopyDirectory(sourceDirectoryName, destinationDirectoryName, showUI, onUserCancel);

    public void CopyFile(string sourceFileName, string destinationFileName) =>
        IOFileSystem.CopyFile(sourceFileName, destinationFileName);

    public void CopyFile(string sourceFileName, string destinationFileName, bool overwrite) =>
        IOFileSystem.CopyFile(sourceFileName, destinationFileName, overwrite);

    public void CopyFile(string sourceFileName, string destinationFileName, UIOption showUI) =>
        IOFileSystem.CopyFile(sourceFileName, destinationFileName, showUI);

    public void CopyFile(string sourceFileName, string destinationFileName, UIOption showUI, UICancelOption onUserCancel) =>
        IOFileSystem.CopyFile(sourceFileName, destinationFileName, showUI, onUserCancel);

    public void CreateDirectory(string directory) => IOFileSystem.CreateDirectory(directory);

    public void DeleteDirectory(string directory, DeleteDirectoryOption onDirectoryNotEmpty) =>
        IOFileSystem.DeleteDirectory(directory, onDirectoryNotEmpty);

    public void DeleteDirectory(string directory, UIOption showUI, RecycleOption recycle) =>
        IOFileSystem.DeleteDirectory(directory, showUI, recycle);

    public void DeleteDirectory(string directory, UIOption showUI, RecycleOption recycle, UICancelOption onUserCancel) =>
        IOFileSystem.DeleteDirectory(directory, showUI, recycle, onUserCancel);

    public void DeleteFile(string file) => IOFileSystem.DeleteFile(file);

    public void DeleteFile(string file, UIOption showUI, RecycleOption recycle, UICancelOption onUserCancel) =>
        IOFileSystem.DeleteFile(file, showUI, recycle, onUserCancel);

    public void DeleteFile(string file, UIOption showUI, RecycleOption recycle) =>
        IOFileSystem.DeleteFile(file, showUI, recycle);

    public bool DirectoryExists(string directory) => IOFileSystem.DirectoryExists(directory);

    public bool FileExists(string file) => IOFileSystem.FileExists(file);

    public ReadOnlyCollection<string> FindInFiles(string directory, string containsText, bool ignoreCase, SearchOption searchType) =>
        IOFileSystem.FindInFiles(directory, containsText, ignoreCase, searchType);

    public ReadOnlyCollection<string> FindInFiles(string directory, string containsText, bool ignoreCase, SearchOption searchType, params string[] fileWildcards) =>
        IOFileSystem.FindInFiles(directory, containsText, ignoreCase, searchType, fileWildcards);

    public ReadOnlyCollection<string> GetDirectories(string directory) => IOFileSystem.GetDirectories(directory);

    public ReadOnlyCollection<string> GetDirectories(string directory, SearchOption searchType, params string[] wildcards) =>
        IOFileSystem.GetDirectories(directory, searchType, wildcards);

    public DirectoryInfo GetDirectoryInfo(string directory) => IOFileSystem.GetDirectoryInfo(directory);

    public DriveInfo GetDriveInfo(string drive) => IOFileSystem.GetDriveInfo(drive);

    public FileInfo GetFileInfo(string file) => IOFileSystem.GetFileInfo(file);

    public ReadOnlyCollection<string> GetFiles(string directory) => IOFileSystem.GetFiles(directory);

    public ReadOnlyCollection<string> GetFiles(string directory, SearchOption searchType, params string[] wildcards) =>
        IOFileSystem.GetFiles(directory, searchType, wildcards);

    public string GetName(string path) => IOFileSystem.GetName(path);

    public string GetParentPath(string path) => IOFileSystem.GetParentPath(path);

    public string GetTempFileName() => IOFileSystem.GetTempFileName();

    public void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName) =>
        IOFileSystem.MoveDirectory(sourceDirectoryName, destinationDirectoryName);

    public void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName, bool overwrite) =>
        IOFileSystem.MoveDirectory(sourceDirectoryName, destinationDirectoryName, overwrite);

    public void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName, UIOption showUI) =>
        IOFileSystem.MoveDirectory(sourceDirectoryName, destinationDirectoryName, showUI);

    public void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName, UIOption showUI, UICancelOption onUserCancel) =>
        IOFileSystem.MoveDirectory(sourceDirectoryName, destinationDirectoryName, showUI, onUserCancel);

    public void MoveFile(string sourceFileName, string destinationFileName) =>
        IOFileSystem.MoveFile(sourceFileName, destinationFileName);

    public void MoveFile(string sourceFileName, string destinationFileName, bool overwrite) =>
        IOFileSystem.MoveFile(sourceFileName, destinationFileName, overwrite);

    public void MoveFile(string sourceFileName, string destinationFileName, UIOption showUI) =>
        IOFileSystem.MoveFile(sourceFileName, destinationFileName, showUI);

    public void MoveFile(string sourceFileName, string destinationFileName, UIOption showUI, UICancelOption onUserCancel) =>
        IOFileSystem.MoveFile(sourceFileName, destinationFileName, showUI, onUserCancel);

    public TextFieldParser OpenTextFieldParser(string file) => IOFileSystem.OpenTextFieldParser(file);

    public TextFieldParser OpenTextFieldParser(string file, params string[] delimiters) => IOFileSystem.OpenTextFieldParser(file, delimiters);

    public TextFieldParser OpenTextFieldParser(string file, params int[] fieldWidths) => IOFileSystem.OpenTextFieldParser(file, fieldWidths);

    public StreamReader OpenTextFileReader(string file) => IOFileSystem.OpenTextFileReader(file);

    public StreamReader OpenTextFileReader(string file, Encoding encoding) => IOFileSystem.OpenTextFileReader(file, encoding);

    public StreamWriter OpenTextFileWriter(string file, bool append) => IOFileSystem.OpenTextFileWriter(file, append);

    public StreamWriter OpenTextFileWriter(string file, bool append, Encoding encoding) => IOFileSystem.OpenTextFileWriter(file, append, encoding);

    public byte[] ReadAllBytes(string file) => IOFileSystem.ReadAllBytes(file);

    public string ReadAllText(string file) => IOFileSystem.ReadAllText(file);

    public string ReadAllText(string file, Encoding encoding) => IOFileSystem.ReadAllText(file, encoding);

    public void RenameDirectory(string directory, string newName) => IOFileSystem.RenameDirectory(directory, newName);

    public void RenameFile(string file, string newName) => IOFileSystem.RenameFile(file, newName);

    public void WriteAllBytes(string file, byte[] data, bool append) => IOFileSystem.WriteAllBytes(file, data, append);

    public void WriteAllText(string file, string text, bool append) => IOFileSystem.WriteAllText(file, text, append);

    public void WriteAllText(string file, string text, bool append, Encoding encoding) => IOFileSystem.WriteAllText(file, text, append, encoding);
}
