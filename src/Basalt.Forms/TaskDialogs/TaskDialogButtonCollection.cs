using System.Collections.ObjectModel;

namespace System.Windows.Forms;

/// <summary>The buttons offered by a <see cref="TaskDialogPage"/>, in the order they are shown.</summary>
public class TaskDialogButtonCollection : Collection<TaskDialogButton>
{
    /// <summary>The page this collection belongs to; set once, by that page's constructor or Buttons setter.</summary>
    internal TaskDialogPage? OwnerPage { get; set; }

    public TaskDialogButtonCollection()
    {
    }

    public TaskDialogButton Add(string? text, bool enabled = true, bool allowCloseDialog = true)
    {
        var button = new TaskDialogButton(text, enabled, allowCloseDialog);
        Add(button);
        return button;
    }

    protected override void ClearItems()
    {
        ThrowIfBound();
        foreach (var button in this) button.OwnerPage = null;
        base.ClearItems();
    }

    protected override void InsertItem(int index, TaskDialogButton item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfBound();
        ThrowIfOwnedElsewhere(item);
        item.OwnerPage = OwnerPage;
        base.InsertItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        ThrowIfBound();
        this[index].OwnerPage = null;
        base.RemoveItem(index);
    }

    protected override void SetItem(int index, TaskDialogButton item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfBound();
        ThrowIfOwnedElsewhere(item);
        this[index].OwnerPage = null;
        item.OwnerPage = OwnerPage;
        base.SetItem(index, item);
    }

    /// <summary>The native task dialog cannot be asked to add, remove or replace a button while it is shown.</summary>
    private void ThrowIfBound()
    {
        if (OwnerPage?.BoundDialog is not null)
        {
            throw new InvalidOperationException("Buttons cannot be added, removed or replaced while the page is shown by a task dialog.");
        }
    }

    private void ThrowIfOwnedElsewhere(TaskDialogButton item)
    {
        if (item.OwnerPage is not null && item.OwnerPage != OwnerPage)
        {
            throw new InvalidOperationException("This button already belongs to another page.");
        }
    }
}
