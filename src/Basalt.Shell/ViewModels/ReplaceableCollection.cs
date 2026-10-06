using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Basalt.Shell.ViewModels;

/// <summary>
/// An observable collection that can swap its whole content with a single
/// notification.
///
/// Replacing the diagnostics of one file used to be a Clear followed by an Add
/// per item, and every Add made each listener (the Problems panel counting
/// severities, the status bar summary) walk the whole list again: N items cost
/// N passes over N items. One Reset event costs each listener one pass.
/// </summary>
public sealed class ReplaceableCollection<T> : ObservableCollection<T>
{
    /// <summary>Replaces everything with <paramref name="items"/>, raising one Reset.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        var fresh = items.ToList();

        CheckReentrancy();
        Items.Clear();
        foreach (var item in fresh) Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Appends <paramref name="items"/> and drops the oldest entries beyond
    /// <paramref name="maxCount"/>, raising one event for the whole batch.
    ///
    /// The Output panel used to Add a line and RemoveAt(0) per line once it
    /// was full, and each RemoveAt shifts the other five thousand entries and
    /// tells the list to rebuild.
    /// </summary>
    public void AddRange(IReadOnlyList<T> items, int maxCount)
    {
        if (items.Count == 0) return;

        CheckReentrancy();
        var start = Items.Count;
        foreach (var item in items) Items.Add(item);

        var excess = Items.Count - maxCount;
        if (excess > 0)
        {
            if (Items is List<T> list) list.RemoveRange(0, excess);
            else for (var i = 0; i < excess; i++) Items.RemoveAt(0);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(excess > 0
            ? new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
            : new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList(), start));
    }
}
