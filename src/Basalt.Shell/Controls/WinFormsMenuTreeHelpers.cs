using Avalonia.Controls;
using Avalonia.Input;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

/// <summary>
/// The cascading-tree painting <see cref="WinFormsMenuDesignEditor"/> (a MenuStrip's own Items,
/// or a ToolStripDropDownButton/SplitButton's own DropDownItems while it hosts
/// <see cref="WinFormsToolStripDropDownPanel"/>'s selection) and
/// <see cref="WinFormsToolStripDropDownPanel"/> (the latter's nested panel) both need: the
/// breadcrumb above an open submenu, stripping "&amp;&amp;"/"&amp;" mnemonics for display, the
/// caption editor under a selected item, "back" to the parent level, and the "Type Here" input
/// that appends a new item. Shared here rather than duplicated so the two editors' trees stay
/// in lockstep; neither class's own test suite had to change for this move.
/// </summary>
internal static class WinFormsMenuTreeHelpers
{
    /// <summary>The caption Visual Studio paints for an item: "&amp;&amp;" is a literal
    /// ampersand, a lone "&amp;" marks the next character as the access key and is never shown.</summary>
    internal static string DisplayText(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '&' && index + 1 < source.Length && source[index + 1] == '&')
            {
                result.Append('&');
                index++;
            }
            else if (source[index] != '&')
            {
                result.Append(source[index]);
            }
        }
        return result.ToString();
    }

    /// <summary>The breadcrumb above an open submenu: every ancestor's display text from
    /// <paramref name="root"/> (exclusive) down to <paramref name="current"/> (inclusive).</summary>
    internal static string OpenPath(IReadOnlyList<WinFormsMenuItem> items, string? current, string root)
    {
        var path = new List<string>();
        while (current is not null && !current.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            var item = items.FirstOrDefault(candidate => candidate.Name == current);
            if (item is null) break;
            path.Add(DisplayText(item.Text));
            current = item.Parent;
        }
        path.Reverse();
        return string.Join(" › ", path);
    }

    /// <summary>Where "Back" from <paramref name="current"/> should land: its parent, or
    /// <paramref name="root"/> once there is no further parent to climb to.</summary>
    internal static string Back(IReadOnlyList<WinFormsMenuItem> items, string? current, string root) =>
        items.FirstOrDefault(item => item.Name == current)?.Parent ?? root;

    /// <summary>
    /// The caption editor Visual Studio shows under a selected, nameable item: a label, a
    /// TextBox pre-filled with its current text, Enter or an Apply button both committing
    /// through <paramref name="commit"/>. A no-op when nothing is selected or
    /// <paramref name="canEditCaption"/> rejects the selection (a separator, or a kind that
    /// paints its own control instead of text).
    /// </summary>
    internal static void AddCaptionEditor(StackPanel rows, IReadOnlyList<WinFormsMenuItem> items,
        string? selectedItemName, Func<WinFormsMenuItem, bool> canEditCaption, string namePrefix,
        Action<WinFormsMenuItem, string> commit)
    {
        var selected = items.FirstOrDefault(item => item.Name == selectedItemName);
        if (selected is null || !canEditCaption(selected))
        {
            return;
        }

        rows.Children.Add(new TextBlock { Text = Localizer.Get(StringKeys.MenuInlineEditText) });
        var caption = new TextBox { Name = namePrefix + "Caption", Text = selected.Text };
        void CommitCaption()
        {
            if (caption.Text is { } text && text != selected.Text)
            {
                commit(selected, text);
            }
        }

        caption.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            CommitCaption();
            args.Handled = true;
        };
        rows.Children.Add(caption);

        var apply = new Button { Name = namePrefix + "ApplyCaption", Content = Localizer.Get(StringKeys.ResourceEditorApply) };
        apply.Click += (_, _) => CommitCaption();
        rows.Children.Add(apply);
    }

    /// <summary>
    /// Visual Studio's own rule for which item a Delete picks up next, applied to a cascading
    /// tree (a MenuStrip's own Items, or a ToolStripDropDownButton/SplitButton's own
    /// DropDownItems): the next sibling, else the previous sibling, else the parent (the open
    /// submenu's own owner item, or <paramref name="root"/> itself once there is no parent item
    /// left to climb to - the caller's selection then simply clears, since root is never one of
    /// its own items). <paramref name="itemsBeforeRemoval"/> must still contain
    /// <paramref name="removedName"/>: the caller computes this before applying the removal.
    /// </summary>
    internal static string SelectionAfterRemoval(IReadOnlyList<WinFormsMenuItem> itemsBeforeRemoval,
        string removedName, string root)
    {
        var removed = itemsBeforeRemoval.FirstOrDefault(item => item.Name == removedName);
        var parent = removed?.Parent ?? root;
        var siblings = itemsBeforeRemoval.Where(item => item.Parent.Equals(parent, StringComparison.OrdinalIgnoreCase)).ToList();
        var index = siblings.FindIndex(item => item.Name == removedName);
        if (index < 0) return parent;
        var next = siblings.Skip(index + 1).FirstOrDefault();
        if (next is not null) return next.Name;
        var previous = siblings.Take(index).LastOrDefault();
        return previous?.Name ?? parent;
    }

    /// <summary>The "Type Here" TextBox Visual Studio shows at the end of a row of items:
    /// trimmed, non-empty Enter text commits through <paramref name="onCommit"/> and clears.</summary>
    internal static TextBox TypeHereInput(string name, Action<string> onCommit)
    {
        var input = new TextBox
        {
            Name = name,
            PlaceholderText = Localizer.Get(StringKeys.MenuInlineTypeHere),
            MinWidth = 90,
        };
        input.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            var text = input.Text.Trim();
            input.Text = "";
            onCommit(text);
            args.Handled = true;
        };
        return input;
    }
}
