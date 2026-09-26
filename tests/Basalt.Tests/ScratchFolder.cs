namespace Basalt.Tests;

/// <summary>
/// Removing a folder a test made, on every platform.
/// </summary>
/// <remarks>
/// Directory.Delete is enough on macOS and Linux and not on Windows, where
/// two things stop it: git writes its objects read-only, and an assembly
/// loaded from a file keeps that file open for as long as the process lives.
/// The first is cured by clearing the attribute; the second cannot be, so a
/// file still in use is left for the system's temporary-file cleanup rather
/// than failing a test that has already passed.
/// </remarks>
internal static class ScratchFolder
{
    public static void Delete(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use: see the remarks.
        }
    }
}
