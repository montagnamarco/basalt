namespace System.Windows.Forms;

/// <summary>Opens a file named by a path or URI a control received (a dropped file, a source document).</summary>
public interface IFileReaderService
{
    Stream OpenFileFromSource(string source);
}
