using System.Collections.ObjectModel;

namespace TaskNova.App.ViewModels;

/// <summary>
/// Brings an ObservableCollection into a desired order with as few changes as possible (insert, remove, move).
/// Rows stay the same objects – selection, scroll position and recycled containers survive live re-sorting.
/// </summary>
public static class ListSync
{
    public static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        // Remove items that are gone.
        if (target.Count > 0)
        {
            var keep = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);
            for (var i = target.Count - 1; i >= 0; i--)
            {
                if (!keep.Contains(target[i]))
                {
                    target.RemoveAt(i);
                }
            }
        }

        // Insert or move into place.
        var present = new HashSet<T>(target, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], item))
            {
                continue;
            }

            if (!present.Contains(item))
            {
                target.Insert(i, item);
                present.Add(item);
                continue;
            }

            var from = IndexOf(target, item, i + 1);
            if (from >= 0)
            {
                target.Move(from, i);
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    /// <summary>
    /// Same, but keeps the selection: Avalonia treats Move as remove + add and would deselect a moved row.
    /// </summary>
    public static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired, Func<T?> getSelected, Action<T?> setSelected)
        where T : class
    {
        var keep = getSelected();
        Sync(target, desired);
        if (keep is not null && !ReferenceEquals(getSelected(), keep) && target.Contains(keep))
        {
            setSelected(keep);
        }
    }

    private static int IndexOf<T>(ObservableCollection<T> list, T item, int start) where T : class
    {
        for (var i = start; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
