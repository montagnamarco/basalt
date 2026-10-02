using System.Collections.ObjectModel;

namespace System.Windows.Forms;

/// <summary>The radio buttons offered by a <see cref="TaskDialogPage"/>; checking one unchecks the others.</summary>
public class TaskDialogRadioButtonCollection : Collection<TaskDialogRadioButton>
{
    internal TaskDialogPage? OwnerPage { get; set; }

    public TaskDialogRadioButtonCollection()
    {
    }

    public TaskDialogRadioButton Add(string? text)
    {
        var radioButton = new TaskDialogRadioButton(text);
        Add(radioButton);
        return radioButton;
    }

    /// <summary>Unchecks every other radio button when one becomes checked; called by TaskDialogRadioButton.Checked.</summary>
    internal void UncheckOthers(TaskDialogRadioButton checkedButton, bool willBeChecked)
    {
        if (!willBeChecked) return;

        foreach (var other in this)
        {
            if (!ReferenceEquals(other, checkedButton)) other.SetChecked(false);
        }
    }

    protected override void ClearItems()
    {
        ThrowIfBound();
        foreach (var radioButton in this) radioButton.OwnerPage = null;
        base.ClearItems();
    }

    protected override void InsertItem(int index, TaskDialogRadioButton item)
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

    protected override void SetItem(int index, TaskDialogRadioButton item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfBound();
        ThrowIfOwnedElsewhere(item);
        this[index].OwnerPage = null;
        item.OwnerPage = OwnerPage;
        base.SetItem(index, item);
    }

    /// <summary>The native task dialog cannot be asked to add, remove or replace a radio button while it is shown.</summary>
    private void ThrowIfBound()
    {
        if (OwnerPage?.BoundDialog is not null)
        {
            throw new InvalidOperationException("Radio buttons cannot be added, removed or replaced while the page is shown by a task dialog.");
        }
    }

    private void ThrowIfOwnedElsewhere(TaskDialogRadioButton item)
    {
        if (item.OwnerPage is not null && item.OwnerPage != OwnerPage)
        {
            throw new InvalidOperationException("This radio button already belongs to another page.");
        }
    }
}
