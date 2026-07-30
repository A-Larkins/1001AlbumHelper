using System;
using System.Collections.Generic;
using System.Linq;

namespace _1001AlbumHelper;

/// <summary>
/// One album row as shown in a list view — the desktop list viewer and the mobile List tab.
/// Rating is blank for the derived lists. Top-level (not nested) so the XAML compiler can bind
/// to it via x:DataType.
/// </summary>
public sealed record ViewRow(string Number, string Rating, string Title, string Artist, string Year)
{
    /// <summary>Everything searchable, flattened once so filtering doesn't redo the work.</summary>
    public string Haystack { get; } =
        $"{Number} {Rating} {Title} {Artist} {Year} " +
        $"{NumberedList.Normalize(Title)} {NumberedList.Normalize(Artist)}";
}

/// <summary>Which column a ViewRow list is ordered by when the user sorts it.</summary>
public enum ViewRowSortColumn
{
    Title,
    Artist,
    Year,
    Rating,
}

/// <summary>Sorting for ViewRow lists — same rules as <see cref="ReplacementCandidates.Sort"/>.</summary>
public static class ViewRowSort
{
    /// <summary>
    /// Orders rows by one column, leaving the underlying list untouched. Titles/artists sort the way
    /// the sheet compares them (case, punctuation and a leading "the" set aside), and a blank cell
    /// always trails, whichever way the column runs.
    /// </summary>
    public static List<ViewRow> Sort(IEnumerable<ViewRow> rows, ViewRowSortColumn column, bool descending)
    {
        if (column == ViewRowSortColumn.Year)
        {
            var keyed = rows.Select(r => (row: r, has: int.TryParse(r.Year.Trim(), out int y), year: y));
            var byPresence = keyed.OrderBy(x => !x.has);
            var ordered = descending
                ? byPresence.ThenByDescending(x => x.year)
                : byPresence.ThenBy(x => x.year);
            return ordered.Select(x => x.row).ToList();
        }
        else
        {
            var keyed = rows.Select(r => (row: r, key: SortKey(r, column)));
            var byPresence = keyed.OrderBy(x => x.key.Length == 0);
            var ordered = descending
                ? byPresence.ThenByDescending(x => x.key, StringComparer.Ordinal)
                : byPresence.ThenBy(x => x.key, StringComparer.Ordinal);
            return ordered.Select(x => x.row).ToList();
        }
    }

    private static string SortKey(ViewRow r, ViewRowSortColumn column) => column switch
    {
        ViewRowSortColumn.Title => NumberedList.Normalize(r.Title),
        ViewRowSortColumn.Artist => NumberedList.Normalize(r.Artist),
        ViewRowSortColumn.Rating => r.Rating.Trim(),
        _ => "",
    };
}
