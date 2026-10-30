using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace _1001AlbumHelper;

/// <summary>
/// One album row as shown in a list view — the desktop list viewer and the mobile List tab.
/// Rating is blank for the derived lists. Top-level (not nested) so the XAML compiler can bind
/// to it via x:DataType.
/// <para>
/// Change-notifying rather than a record because the row remembers whether it's been sent to a
/// playlist. That state has to live on the row and not on the button: the lists virtualize, so a
/// button whose label was set directly would carry that label onto whichever row its container was
/// later recycled for.
/// </para>
/// </summary>
public sealed class ViewRow : INotifyPropertyChanged
{
    /// <summary>Null until the ＋ button is pressed; then true if it was added, false if it was already there.</summary>
    private bool? _queued;
    private string _rating;

    public ViewRow(string number, string rating, string title, string artist, string year,
                   int playlist = 1, int sheetRow = 0)
    {
        Number = number;
        _rating = rating;
        SheetRow = sheetRow;
        Title = title;
        Artist = artist;
        Year = year;
        Playlist = playlist;
        Haystack = $"{number} {rating} {title} {artist} {year} " +
                   $"{NumberedList.Normalize(title)} {NumberedList.Normalize(artist)}";
    }

    public string Number { get; }

    /// <summary>
    /// The rating shown in the row. Settable because re-rating an album from the browse list has
    /// to show up there straight away — the alternative is re-reading the whole sheet for one cell.
    /// </summary>
    public string Rating
    {
        get => _rating;
        set
        {
            if (_rating == value) return;
            _rating = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rating)));
        }
    }

    /// <summary>
    /// This album's 1-indexed row on the master sheet, or 0 for rows that don't have one — the
    /// Must Hear and replacements lists, and the phone's offline snapshot. Non-zero is what makes
    /// a row rateable, since the rater writes to <c>B{SheetRow}</c>.
    /// </summary>
    public int SheetRow { get; }

    /// <summary>Whether this row can be sent to the rater — see <see cref="SheetRow"/>.</summary>
    public bool CanRate => SheetRow > 0;

    public string Title { get; }
    public string Artist { get; }
    public string Year { get; }

    /// <summary>
    /// Which working playlist this row's ＋ button feeds: 1 for the 1001 and Must Hear, 2 for the
    /// replacements list — those are the recommendations PLAYLIST2 exists for.
    /// </summary>
    public int Playlist { get; }

    /// <summary>Everything searchable, flattened once so filtering doesn't redo the work.</summary>
    public string Haystack { get; }

    /// <summary>The ＋ button's label: "＋ P2" until pressed, then "✓ P2" added or "· P2" already there.</summary>
    public string PlaylistLabel => $"{(_queued is null ? "＋" : _queued.Value ? "✓" : "·")} P{Playlist}";

    public string PlaylistTip => $"Add to Playlist {Playlist}.";

    /// <summary>False once the row has been sent, so it can't be queued twice.</summary>
    public bool CanQueue => _queued is null;

    /// <summary>Records what pressing ＋ did, so the button can show it and stop offering.</summary>
    public void MarkQueued(bool added)
    {
        _queued = added;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlaylistLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanQueue)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Where the 1001 picks up: the first blank row after the furthest one that carries any mark —
    /// a rating or a "✓ listened". Rows skipped further back don't count, since the list is worked
    /// through in order and those are leftovers, not the place reached. 0 when nothing is marked;
    /// the last row when everything is.
    /// </summary>
    public static int ResumeIndex(IReadOnlyList<ViewRow> rows)
    {
        int lastMarked = -1;
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(rows[i].Rating)) { lastMarked = i; break; }
        }

        // Everything after the furthest mark is blank by definition, so the next row is the one.
        return Math.Min(lastMarked + 1, Math.Max(0, rows.Count - 1));
    }
}

/// <summary>Which column a ViewRow list is ordered by when the user sorts it.</summary>
public enum ViewRowSortColumn
{
    /// <summary>The list's own order — what the sheet holds, and where the list opens.</summary>
    Number,
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
        if (column == ViewRowSortColumn.Number)
        {
            // Numeric, not textual: "10" sorts after "9", not between "1" and "2". A row without a
            // usable number trails, the same way a blank cell does in every other column.
            var keyed = rows.Select(r => (row: r, has: int.TryParse(r.Number.Trim(), out int n), number: n));
            var byPresence = keyed.OrderBy(x => !x.has);
            var ordered = descending
                ? byPresence.ThenByDescending(x => x.number)
                : byPresence.ThenBy(x => x.number);
            return ordered.Select(x => x.row).ToList();
        }
        else if (column == ViewRowSortColumn.Year)
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
