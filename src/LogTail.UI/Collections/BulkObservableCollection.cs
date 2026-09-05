using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace LogTail.UI.Collections;

/// <summary>
/// ObservableCollection with single-notification bulk operations for
/// large log lists. Per-item Add/RemoveAt fires N UI updates which
/// janks at 50k items; these helpers mutate once and raise one Reset.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Append many items with a single Reset notification.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var list = items as IReadOnlyList<T> ?? items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        foreach (var item in list)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Remove count items from the front with a single Reset notification.
    /// O(n) once via List.RemoveRange instead of O(n*m) RemoveAt(0) loop.
    /// </summary>
    public void RemoveFromFront(int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (count >= Count)
        {
            Clear();
            return;
        }

        if (Items is List<T> list)
        {
            list.RemoveRange(0, count);
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                Items.RemoveAt(0);
            }
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
