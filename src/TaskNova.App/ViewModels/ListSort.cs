using TaskNova.Core.Storage;

namespace TaskNova.App.ViewModels;

/// <summary>
/// Sort state of one list (column + direction), remembered per list in the settings ("startup" → "name:asc").
/// Click on a header: same column → direction flips; new column → names ascending, values descending first.
/// </summary>
public sealed class ListSort
{
    private readonly string _listId;
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly IReadOnlySet<string> _descendingFirst;

    public ListSort(string listId, AppSettings settings, Action save, string defaultColumn, bool defaultDescending,
        params string[] descendingFirst)
    {
        _listId = listId;
        _settings = settings;
        _save = save;
        _descendingFirst = new HashSet<string>(descendingFirst, StringComparer.Ordinal);
        Column = defaultColumn;
        Descending = defaultDescending;
        if (settings.ListSorts.TryGetValue(listId, out var stored) && stored.Split(':') is [var column, var direction] && column.Length > 0)
        {
            Column = column;
            Descending = direction == "desc";
        }
    }

    public string Column { get; private set; }

    public bool Descending { get; private set; }

    public void Toggle(string column)
    {
        if (Column == column)
        {
            Descending = !Descending;
        }
        else
        {
            Column = column;
            Descending = _descendingFirst.Contains(column);
        }

        _settings.ListSorts[_listId] = $"{Column}:{(Descending ? "desc" : "asc")}";
        _save();
    }

    /// <summary>Comparison for the current column; unknown columns use the first key; ties fall back to <paramref name="tieBreak"/>.</summary>
    public Comparison<T> Comparer<T>(IReadOnlyDictionary<string, Comparison<T>> columns, Comparison<T> tieBreak)
    {
        var compare = columns.TryGetValue(Column, out var c) ? c : columns.Values.First();
        var descending = Descending;
        return (a, b) =>
        {
            var result = compare(a, b);
            if (descending)
            {
                result = -result;
            }

            return result != 0 ? result : tieBreak(a, b);
        };
    }

    public static int Text(string? a, string? b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
}
