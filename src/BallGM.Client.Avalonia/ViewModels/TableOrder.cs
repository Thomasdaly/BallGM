namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>How every sortable stat table orders and marks its rows.</summary>
public static class TableOrder
{
    /// <summary>
    /// Rows by a key, either way. A row with nothing to rank (no games, no attempts) comes after every
    /// real figure whichever way the column runs — "no figure" is neither high nor low.
    /// </summary>
    public static IOrderedEnumerable<T> NullsLast<T>(IEnumerable<T> rows, Func<T, double?> key, bool descending)
    {
        var ranked = rows.OrderBy(row => key(row) is null);
        return descending ? ranked.ThenByDescending(key) : ranked.ThenBy(key);
    }

    /// <summary>The arrow a header wears when its table is sorted by it.</summary>
    public static string Glyph(bool descending) => descending ? " ▾" : " ▴";
}
