using System.Runtime.InteropServices;
using System.Text;

namespace Basalt.Workspace;

/// <summary>
/// One spelling for one file.
///
/// A file can be reached through a symbolic link (/tmp is /private/tmp on
/// macOS), in the wrong case on a case-insensitive disk, or by an 8.3 short
/// name on Windows. Anything that compares paths (buffers against the files a
/// tool names, an open tab against a change) compares this form, or it opens
/// a second tab, refuses its own edit as stale, or finds nothing.
/// </summary>
public static class PathIdentity
{
    private const int MaxLinkHops = 40;

    private static readonly bool CaseInsensitiveDisk = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>How paths compare: by the disk's rules, since a name that does not exist yet keeps the case it was written in.</summary>
    public static StringComparer Comparer { get; } =
        CaseInsensitiveDisk ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The comparison matching <see cref="Comparer"/>.</summary>
    public static StringComparison Comparison { get; } =
        CaseInsensitiveDisk ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The path with every symbolic link followed and every segment spelled as
    /// the disk spells it. Segments that do not exist are kept as written.
    /// </summary>
    /// <exception cref="InvalidOperationException">Too many links, or a stream name on Windows.</exception>
    public static string Canonical(string path) => Resolve(Path.GetFullPath(path), 0);

    /// <summary>Whether two paths name the same file.</summary>
    public static bool Same(string a, string b)
    {
        try
        {
            return string.Equals(Canonical(a), Canonical(b), Comparison);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // A name that cannot be resolved (a link loop, a stream name) is
            // the same file only as written.
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), Comparison);
        }
    }

    private static string Resolve(string fullPath, int hops)
    {
        if (hops > MaxLinkHops) throw new InvalidOperationException("Too many symbolic links.");

        var root = Path.GetPathRoot(fullPath) ?? "";

        if (OperatingSystem.IsWindows())
        {
            // "file.txt:stream" names a hidden stream of the file, a way to
            // reach the same bytes by a name no check recognises.
            if (fullPath[root.Length..].Contains(':'))
                throw new InvalidOperationException("Alternate data streams are not available.");

            fullPath = ToLongName(fullPath);
            root = Path.GetPathRoot(fullPath) ?? "";
        }

        var current = NormalizeRoot(root, OperatingSystem.IsWindows());

        var segments = fullPath[root.Length..]
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(s => s.Length > 0);

        foreach (var segment in segments)
        {
            var name = CaseInsensitiveDisk ? OnDiskName(current, segment) : segment;
            var next = Path.Combine(current, name);

            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);

            if (info.LinkTarget is not null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);

                // A broken link has no target to resolve; it still points
                // somewhere, and that somewhere is what gets checked.
                var targetPath = target?.FullName ?? Path.GetFullPath(info.LinkTarget, current);

                next = Resolve(Path.GetFullPath(targetPath), hops + 1);
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// "c:\" and "C:\" are the same drive: Windows spells it as the user
    /// typed it, so it is brought to one case. Taking the platform as a
    /// parameter lets this be tested anywhere.
    /// </summary>
    internal static string NormalizeRoot(string root, bool windows) =>
        windows && root.Length >= 2 && root[1] == ':' && char.IsLetter(root[0])
            ? char.ToUpperInvariant(root[0]) + root[1..]
            : root;

    /// <summary>The name a folder entry really has, whatever case it was asked for in.</summary>
    private static string OnDiskName(string directory, string segment)
    {
        try
        {
            string? caseless = null;

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);

                if (string.Equals(name, segment, StringComparison.Ordinal)) return name;

                if (caseless is null && string.Equals(name, segment, StringComparison.OrdinalIgnoreCase))
                    caseless = name;
            }

            return caseless ?? segment;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return segment;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetLongPathNameW")]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint size);

    /// <summary>Expands 8.3 short names ("PROGRA~1") to the names a check can recognise.</summary>
    private static string ToLongName(string path)
    {
        if (!path.Contains('~') || !OperatingSystem.IsWindows()) return path;

        try
        {
            var buffer = new StringBuilder(1024);
            var length = GetLongPathName(path, buffer, (uint)buffer.Capacity);

            return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return path;
        }
    }
}

/// <summary>
/// Decides whether paths are the same file, cheaply and with each path
/// resolved at most once.
///
/// Resolving a path lists every parent folder on a case-insensitive disk, so
/// asking it of every open document for every comparison, on the interface
/// thread, froze the editor. Here two paths that already agree are the same
/// without touching the disk; two whose file names differ are not; and only a
/// pair that might be the same file through a link or another case is
/// resolved, once each.
/// </summary>
public sealed class PathMatcher
{
    private readonly Func<string, string> _canonical;
    private readonly Dictionary<string, string> _resolved = new(PathIdentity.Comparer);

    public PathMatcher(Func<string, string>? canonical = null) =>
        _canonical = canonical ?? PathIdentity.Canonical;

    public bool Same(string a, string b)
    {
        string fullA, fullB;

        try
        {
            fullA = Path.GetFullPath(a);
            fullB = Path.GetFullPath(b);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (string.Equals(fullA, fullB, PathIdentity.Comparison)) return true;

        // A link to a folder keeps the file's name; a file reached under
        // another name is the one case this does not see, and it costs a
        // second tab rather than a wrong edit.
        if (!string.Equals(Path.GetFileName(fullA), Path.GetFileName(fullB), PathIdentity.Comparison)) return false;

        return string.Equals(Resolve(fullA), Resolve(fullB), PathIdentity.Comparison);
    }

    private string Resolve(string fullPath)
    {
        if (_resolved.TryGetValue(fullPath, out var known)) return known;

        try
        {
            known = _canonical(fullPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            known = fullPath;
        }

        return _resolved[fullPath] = known;
    }
}
