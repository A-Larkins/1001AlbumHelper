namespace _1001AlbumHelper;

/// <summary>
/// Ratings saved during this run of the app, remembered so any list still showing an older copy of
/// an album can catch up.
/// <para>
/// The phone's 1001 list is a snapshot baked into the app at build time, and the live lists are
/// cached for the session — so without this, an album rated on the Rate tab keeps showing its old
/// rating over on the List tab. Recording each save here lets those lists correct themselves
/// without re-reading the whole sheet.
/// </para>
/// <para>
/// Keyed on title + artist rather than sheet row, because the snapshot rows don't carry one.
/// Matching is <see cref="NumberedList.Normalize"/>'s, so punctuation and case drifting between the
/// snapshot and the sheet don't hide a change.
/// </para>
/// </summary>
public static class RatingChanges
{
    private static readonly Dictionary<string, string> _ratings = new();

    /// <summary>Notes that an album now carries <paramref name="rating"/>.</summary>
    public static void Record(string title, string artist, string rating)
    {
        lock (_ratings) _ratings[Key(title, artist)] = rating;
    }

    /// <summary>What this album was rated during this run, or null if it wasn't rated here.</summary>
    public static string? For(string title, string artist)
    {
        lock (_ratings) return _ratings.TryGetValue(Key(title, artist), out string? r) ? r : null;
    }

    /// <summary>Brings rows up to date. Rows not rated during this run are left exactly as they are.</summary>
    public static void ApplyTo(IEnumerable<ViewRow> rows)
    {
        foreach (var row in rows)
            if (For(row.Title, row.Artist) is { } rating) row.Rating = rating;
    }

    /// <summary>How many albums have been rated this run — 0 means no list needs correcting.</summary>
    public static int Count
    {
        get { lock (_ratings) return _ratings.Count; }
    }

    /// <summary>Forgets everything recorded. For tests, which must not leak state into each other.</summary>
    public static void Reset()
    {
        lock (_ratings) _ratings.Clear();
    }

    private static string Key(string title, string artist) =>
        $"{NumberedList.Normalize(title)}|{NumberedList.Normalize(artist)}";
}
