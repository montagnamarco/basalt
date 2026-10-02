using System.Globalization;
using System.IO;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// Combining several strips into one (a document-specific toolbar merged
/// into the main window's when the document activates, as MDI does), and
/// undoing it; and saving/restoring a form's toolstrips' Visible and
/// Location between sessions.
/// </summary>
/// <remarks>
/// Every item moves with MergeAction.Append semantics (appended to the
/// target, in the source's own order): ToolStripItem.MergeAction and
/// MergeIndex, which would let an item ask to be inserted or replace one of
/// the target's, are not implemented here -- they belong to ToolStripItem,
/// owned by another part of this wave; see the task's report.
/// </remarks>
public static partial class ToolStripManager
{
    /// <summary>One call to <see cref="Merge(ToolStrip, ToolStrip)"/>: enough to undo exactly that call.</summary>
    private sealed record MergeRecord(ToolStrip Source, List<(ToolStripItem Item, int Index)> Items);

    private static readonly Dictionary<ToolStrip, List<MergeRecord>> s_merges = [];

    public static ToolStrip? FindToolStrip(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (s_stripsLock)
        {
            foreach (var reference in s_strips)
            {
                if (reference.TryGetTarget(out var strip) && !strip.IsDisposed && strip.Name == name) return strip;
            }
        }

        return null;
    }

    /// <summary>
    /// A shortcut combination Windows itself can represent: a plain letter
    /// or digit alone cannot (it would swallow ordinary typing), but one
    /// with Ctrl or Alt, or a function/navigation key alone, can.
    /// </summary>
    public static bool IsValidShortcut(Keys shortcut)
    {
        if (shortcut == Keys.None) return false;

        var code = shortcut & Keys.KeyCode;
        var modifiers = shortcut & Keys.Modifiers;

        if (modifiers is Keys.Control or Keys.Alt or Keys.Control or (Keys.Control | Keys.Shift) or (Keys.Alt | Keys.Shift) or (Keys.Control | Keys.Alt) or (Keys.Control | Keys.Alt | Keys.Shift))
        {
            return code is not (Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu);
        }

        // No Ctrl/Alt: only a key that does not type anything may stand alone (Shift+letter still types).
        return code is >= Keys.F1 and <= Keys.F24
            || code is Keys.Insert or Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown;
    }

    /// <summary>Whether some tracked strip's item (recursively through its drop-downs) already claims this shortcut.</summary>
    public static bool IsShortcutDefined(Keys shortcut)
    {
        List<ToolStrip> strips = [];
        lock (s_stripsLock)
        {
            foreach (var reference in s_strips)
            {
                if (reference.TryGetTarget(out var strip) && !strip.IsDisposed) strips.Add(strip);
            }
        }

        return strips.Any(strip => strip.Items.Cast<ToolStripItem>().Any(item => DefinesShortcut(item, shortcut)));
    }

    private static bool DefinesShortcut(ToolStripItem item, Keys shortcut)
    {
        if (item is ToolStripMenuItem { ShortcutKeys: var keys } && keys == shortcut) return true;
        if (item is not ToolStripDropDownItem { HasDropDown: true } dropDown) return false;

        return dropDown.DropDownItems.Cast<ToolStripItem>().Any(child => DefinesShortcut(child, shortcut));
    }

    /// <summary>Moves every item of <paramref name="sourceToolStrip"/> onto the end of <paramref name="targetToolStrip"/>, remembering where to put each one back.</summary>
    public static bool Merge(ToolStrip sourceToolStrip, ToolStrip targetToolStrip)
    {
        ArgumentNullException.ThrowIfNull(sourceToolStrip);
        ArgumentNullException.ThrowIfNull(targetToolStrip);

        if (!sourceToolStrip.AllowMerge || sourceToolStrip.Items.Count == 0) return false;

        var moved = new List<(ToolStripItem Item, int Index)>();
        foreach (ToolStripItem item in sourceToolStrip.Items.Cast<ToolStripItem>().ToList())
        {
            moved.Add((item, sourceToolStrip.Items.IndexOf(item)));
        }

        foreach (var (item, _) in moved)
        {
            sourceToolStrip.Items.Remove(item);
            targetToolStrip.Items.Add(item);
        }

        if (!s_merges.TryGetValue(targetToolStrip, out var list)) s_merges[targetToolStrip] = list = [];
        list.Add(new MergeRecord(sourceToolStrip, moved));

        return true;
    }

    public static bool Merge(ToolStrip sourceToolStrip, string targetName)
    {
        ArgumentNullException.ThrowIfNull(sourceToolStrip);

        return FindToolStrip(targetName) is { } target && Merge(sourceToolStrip, target);
    }

    public static bool RevertMerge(ToolStrip targetToolStrip)
    {
        ArgumentNullException.ThrowIfNull(targetToolStrip);

        if (!s_merges.TryGetValue(targetToolStrip, out var list) || list.Count == 0) return false;

        foreach (var record in list) Unmerge(targetToolStrip, record);
        s_merges.Remove(targetToolStrip);

        return true;
    }

    public static bool RevertMerge(ToolStrip targetToolStrip, ToolStrip sourceToolStrip)
    {
        ArgumentNullException.ThrowIfNull(targetToolStrip);
        ArgumentNullException.ThrowIfNull(sourceToolStrip);

        if (!s_merges.TryGetValue(targetToolStrip, out var list)) return false;

        var record = list.LastOrDefault(candidate => candidate.Source == sourceToolStrip);
        if (record is null) return false;

        Unmerge(targetToolStrip, record);
        list.Remove(record);
        if (list.Count == 0) s_merges.Remove(targetToolStrip);

        return true;
    }

    public static bool RevertMerge(string targetName) => FindToolStrip(targetName) is { } target && RevertMerge(target);

    private static void Unmerge(ToolStrip target, MergeRecord record)
    {
        foreach (var (item, index) in record.Items)
        {
            // An item a later merge (or an out-of-order RevertMerge) already moved elsewhere is left where
            // it is: moving it back here from a slot it no longer occupies would only trade one stale
            // reference for another. Reverting in the reverse order the merges happened avoids this.
            if (!ReferenceEquals(item.Owner, target)) continue;

            target.Items.Remove(item);
            record.Source.Items.Insert(Math.Min(index, record.Source.Items.Count), item);
        }
    }

    // --- Settings: one line per named ToolStrip (Visible, X, Y), under Application.UserAppDataPath. ---

    public static void SaveSettings(Form targetForm) => SaveSettings(targetForm, targetForm?.Text ?? string.Empty);

    public static void SaveSettings(Form targetForm, string settingsKey)
    {
        ArgumentNullException.ThrowIfNull(targetForm);
        ArgumentNullException.ThrowIfNull(settingsKey);

        var path = SettingsPath(settingsKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var lines = StripsOn(targetForm)
            .Where(strip => !string.IsNullOrEmpty(strip.Name))
            .Select(strip => string.Join('\t', strip.Name, strip.Visible, strip.Location.X.ToString(CultureInfo.InvariantCulture), strip.Location.Y.ToString(CultureInfo.InvariantCulture)));

        File.WriteAllLines(path, lines);
    }

    public static void LoadSettings(Form targetForm) => LoadSettings(targetForm, targetForm?.Text ?? string.Empty);

    public static void LoadSettings(Form targetForm, string settingsKey)
    {
        ArgumentNullException.ThrowIfNull(targetForm);
        ArgumentNullException.ThrowIfNull(settingsKey);

        var path = SettingsPath(settingsKey);
        if (!File.Exists(path)) return;

        // The first strip with a given name wins ties, the same as WinForms' own control-name lookup:
        // a plain ToDictionary would throw on two strips that (however unusually) share a Name.
        var byName = new Dictionary<string, ToolStrip>();
        foreach (var strip in StripsOn(targetForm))
        {
            if (!string.IsNullOrEmpty(strip.Name)) byName.TryAdd(strip.Name, strip);
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('\t');
            if (parts.Length != 4 || !byName.TryGetValue(parts[0], out var strip)) continue;
            if (!bool.TryParse(parts[1], out var visible)) continue;
            if (!int.TryParse(parts[2], CultureInfo.InvariantCulture, out var x)) continue;
            if (!int.TryParse(parts[3], CultureInfo.InvariantCulture, out var y)) continue;

            strip.Visible = visible;
            strip.Location = new Drawing.Point(x, y);
        }
    }

    private static IEnumerable<ToolStrip> StripsOn(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is ToolStrip strip) yield return strip;

            foreach (var descendant in StripsOn(child)) yield return descendant;
        }
    }

    /// <summary>
    /// Where <see cref="SaveSettings(Form)"/> and <see cref="LoadSettings(Form)"/>
    /// read and write, normally <see cref="Application.UserAppDataPath"/>.
    /// Settable only from this assembly, so a test can point it at a
    /// throwaway folder instead of the real user's application data.
    /// </summary>
    internal static string? SettingsRootOverride { get; set; }

    private static string SettingsPath(string settingsKey)
    {
        var fileName = string.Join('_', settingsKey.Split(Path.GetInvalidFileNameChars()));
        if (fileName.Length == 0) fileName = "Default";

        var root = SettingsRootOverride ?? Application.UserAppDataPath;
        return Path.Combine(root, "ToolStripSettings", fileName + ".settings");
    }
}
