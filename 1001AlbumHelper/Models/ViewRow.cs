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

    public ViewRow(string number, string rating, string title, string artist, string year, int playlist = 1)
    {
        Number = number;
        Rating = rating;
        Title = title;
        Artist = artist;
        Year = year;
        Playlist = playlist;
        Haystack = $"{number} {rating} {title} {artist} {year} " +
                   $"{NumberedList.Normalize(title)} {NumberedList.Normalize(artist)}";
    }

    public string Number { get; }
    public string Rating { get; }
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
