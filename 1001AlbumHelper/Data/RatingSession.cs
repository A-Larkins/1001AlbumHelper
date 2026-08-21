namespace _1001AlbumHelper;

/// <summary>Which albums a rating session works through.</summary>
public enum RatingMode
{
    /// <summary>Albums with no mark at all — the ones not listened to yet.</summary>
    NextUp,

    /// <summary>Albums marked "listened" (✓) but never given a real rating.</summary>
    Backfill,

    /// <summary>Albums that already carry one of the four ratings — for changing your mind.</summary>
    Revisit,
}

/// <summary>One album row on the master list.</summary>
public sealed record AlbumEntry(
    int SheetRow,   // 1-indexed row in the sheet, so the rating cell is B{SheetRow}
    string Number,  // the "#" column, as shown on the list
    string Rating,
    string Title,
    string Artist,
    string Year,
    string Genre = ""); // column F — comma-joined Discogs genres, blank until backfilled

/// <summary>
/// A live rating run over the master album list.
/// <para>
/// The queue is built from a fresh read of the sheet rather than the downloaded CSV, so row
/// positions can never be stale, and each rating is written back as a single-cell update that
/// leaves the rest of the sheet — including its formatting — alone.
/// </para>
/// </summary>
public sealed class RatingSession
{
    public const string Starred = "⭐";
    public const string Liked = "👍";
    public const string Disliked = "👎";
    public const string Trash = "❌";
    public const string Listened = "✓";

    /// <summary>The four ratings, in the order the legend lists them.</summary>
    public static readonly (string Symbol, string Label, string Blurb)[] Choices =
    {
        (Starred,  "Really enjoyable/favs", "Only bangers and/or perfect albums."),
        (Liked,    "Mostly enjoyable",      "Good stuff, but not a favorite or not a perfect album."),
        (Disliked, "Mostly sucks",          "Probably means the singer is bad."),
        (Trash,    "Trash",                 "Actual torture to get through."),
    };

    private readonly ISheetsClient _writer;
    private readonly string _tab;
    private readonly string _mustHearTab;
    private readonly List<AlbumEntry> _all;
    private List<AlbumEntry> _queue = new();
    private int _index;

    private RatingSession(ISheetsClient writer, string tab, string mustHearTab, List<AlbumEntry> all)
    {
        _writer = writer;
        _tab = tab;
        _mustHearTab = mustHearTab;
        _all = all;
    }

    public RatingMode Mode { get; private set; }
    public bool Shuffle { get; private set; }

    /// <summary>In <see cref="RatingMode.Revisit"/>, the one rating being revisited — null for all four.</summary>
    public string? RatingFilter { get; private set; }

    /// <summary>Total rows on the master list.</summary>
    public int TotalAlbums => _all.Count;

    /// <summary>Every row on the master list, in sheet order.</summary>
    public IReadOnlyList<AlbumEntry> AllAlbums => _all;

    /// <summary>How many albums are still waiting in the current queue, including the current one.</summary>
    public int Remaining => Math.Max(0, _queue.Count - _index);

    /// <summary>The album awaiting a rating, or null when the queue is finished.</summary>
    public AlbumEntry? Current => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;

    /// <summary>The album after <see cref="Current"/>, or null at the end — used to warm its cover art.</summary>
    public AlbumEntry? Next => _index + 1 < _queue.Count ? _queue[_index + 1] : null;

    /// <summary>Whether <see cref="Back"/> has anything to step back to.</summary>
    public bool CanGoBack => _index > 0;

    /// <summary>Reads the master list and prepares a session. Read-only.</summary>
    public static async Task<RatingSession> LoadAsync(ISheetsClient writer, string tab, string mustHearTab)
    {
        var rows = await writer.ReadTabAsync(tab, "A1:F");

        // Find the header row ("#", "Rating", …) so the legend block above it is skipped
        // without hardcoding how tall it is.
        int header = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count > 0 && rows[i][0].Trim() == "#") { header = i; break; }
        }
        if (header < 0)
            throw new InvalidOperationException(
                $"Couldn't find the header row (a cell reading \"#\") on tab \"{tab}\".");

        var all = new List<AlbumEntry>();
        for (int i = header + 1; i < rows.Count; i++)
        {
            var r = rows[i];
            string Cell(int c) => c < r.Count ? r[c].Trim() : "";

            // Skip anything that isn't a numbered album row.
            if (!int.TryParse(Cell(0), out _)) continue;

            all.Add(new AlbumEntry(
                SheetRow: i + 1, // sheet rows are 1-indexed
                Number: Cell(0),
                Rating: Cell(1),
                Title: Cell(2),
                Artist: Cell(3),
                Year: Cell(4),
                Genre: Cell(5)));
        }

        return new RatingSession(writer, tab, mustHearTab, all);
    }

    /// <summary>
    /// Rebuilds the queue for a mode/order. Albums rated during this session drop out — except in
    /// <see cref="RatingMode.Revisit"/>, whose queue is defined by a rating being *present*, so a
    /// re-rated album stays in it (under the new symbol) rather than vanishing mid-run.
    /// <para>
    /// <paramref name="ratingFilter"/> narrows Revisit to a single symbol. The other two modes
    /// ignore it: their queues are defined by the rating being absent.
    /// </para>
    /// </summary>
    public void Rebuild(RatingMode mode, bool shuffle, string? ratingFilter = null)
    {
        Mode = mode;
        Shuffle = shuffle;
        RatingFilter = mode == RatingMode.Revisit ? ratingFilter : null;

        var matching = _all.Where(a => mode switch
        {
            RatingMode.NextUp => string.IsNullOrWhiteSpace(a.Rating),
            RatingMode.Backfill => a.Rating == Listened,
            _ => RatingFilter is null ? IsRated(a.Rating) : a.Rating == RatingFilter,
        });

        // Ascending means "by position on the list", which is what the # column encodes.
        _queue = matching.OrderBy(a => a.SheetRow).ToList();
        if (shuffle) Shuffle_(_queue);

        _index = 0;
    }

    private static void Shuffle_(List<AlbumEntry> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Leaves the current album untouched and moves to the next one.</summary>
    public void Skip()
    {
        if (_index < _queue.Count) _index++;
    }

    /// <summary>
    /// Steps back to the previous album in the queue, whether it was skipped or already rated —
    /// letting a skip or a mis-tap be corrected instead of being stuck moving only forward.
    /// </summary>
    public void Back()
    {
        if (_index > 0) _index--;
    }

    /// <summary>
    /// True for the four real ratings. A blank cell and a "✓ listened" mark are both *not* a
    /// rating — they're what Next up and Backfill exist to clear.
    /// </summary>
    public static bool IsRated(string rating) =>
        Choices.Any(c => c.Symbol == rating.Trim());

    /// <summary>
    /// Points the session at one album, whatever its rating — how an album that's already rated
    /// gets corrected without walking a queue to reach it. An album the current queue doesn't hold
    /// is spliced in at the current position, so carrying on afterwards resumes the queue where it
    /// was. False if no row on the master list has that number.
    /// </summary>
    public bool FocusOn(int sheetRow)
    {
        int at = _queue.FindIndex(a => a.SheetRow == sheetRow);
        if (at >= 0) { _index = at; return true; }

        var album = _all.FirstOrDefault(a => a.SheetRow == sheetRow);
        if (album is null) return false;

        _index = Math.Clamp(_index, 0, _queue.Count);
        _queue.Insert(_index, album);
        return true;
    }

    /// <summary>
    /// Albums whose title, artist or year matches every whitespace-separated term in
    /// <paramref name="query"/> — the same narrowing rule the browse lists' search boxes use, so
    /// "nirvana 1993" cuts down rather than widening. A blank query matches nothing, not everything.
    /// </summary>
    public IReadOnlyList<AlbumEntry> Search(string query, int limit = 20)
    {
        var terms = NumberedList.Normalize(query ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return Array.Empty<AlbumEntry>();

        return _all
            .Where(a =>
            {
                string hay = $"{NumberedList.Normalize(a.Title)} {NumberedList.Normalize(a.Artist)} {a.Year}";
                return terms.All(t => hay.Contains(t, StringComparison.Ordinal));
            })
            .Take(limit)
            .ToList();
    }

    /// <summary>What a rating did: the cell written, plus how the Must Hear list reacted.</summary>
    public sealed record RateResult(AlbumEntry Album, string Cell, string? MustHearNote);

    /// <summary>
    /// Writes <paramref name="rating"/> to the current album's cell and advances. A ⭐ also gets
    /// added to the Must Hear list, slotted in by year and renumbered.
    /// </summary>
    public async Task<RateResult> RateCurrentAsync(string rating)
    {
        var album = Current ?? throw new InvalidOperationException("Nothing left to rate.");

        // Guard against writing to a row that has shifted under us: the in-memory snapshot and
        // the sheet must still agree on what lives at this row.
        var live = await _writer.ReadTabAsync(_tab, $"A{album.SheetRow}:E{album.SheetRow}");
        string liveTitle = live.Count > 0 && live[0].Count > 2 ? live[0][2].Trim() : "";
        if (!string.Equals(liveTitle, album.Title, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Row {album.SheetRow} now holds \"{liveTitle}\" but this session expected " +
                $"\"{album.Title}\". The sheet changed — reopen the rater to reload it.");

        // The rating being replaced, read from the master snapshot rather than the queue entry —
        // the queue still holds the album as it looked when the queue was built.
        string previous = (_all.FirstOrDefault(a => a.SheetRow == album.SheetRow) ?? album).Rating;

        string cell = $"B{album.SheetRow}";
        await _writer.UpdateCellAsync(_tab, cell, rating);
        Console.WriteLine($"{rating} → {cell}  {album.Title} — {album.Artist} ({album.Year})");

        // Keep both in-memory copies in step: the master snapshot so a rebuild won't offer this
        // album again, and the queue entry so stepping Back onto it shows what's now on the sheet.
        int at = _all.FindIndex(a => a.SheetRow == album.SheetRow);
        if (at >= 0) _all[at] = _all[at] with { Rating = rating };
        _queue[_index] = _queue[_index] with { Rating = rating };

        // …and tell the rest of the app, whose lists may be showing an older copy of this album.
        RatingChanges.Record(album.Title, album.Artist, rating);

        _index++;

        // A star earns a place on the Must Hear list — and a star taken away gives that place back,
        // so re-rating a ⭐ down doesn't leave the album on a list it no longer belongs on. Both
        // sides are deliberately best-effort: the rating itself is already saved, so a failure here
        // must not look like a failed rating.
        bool starGained = rating == Starred;
        bool starLost = previous == Starred && rating != Starred;

        string? note = null;
        if (starGained || starLost)
        {
            string what = starGained ? "adding to" : "removing from";
            Console.WriteLine($"{(starGained ? Starred : previous)} {album.Title} — {what} \"{_mustHearTab}\"…");
            try
            {
                note = starGained ? await AddToMustHearAsync(album) : await RemoveFromMustHearAsync(album);
                Console.WriteLine($"   {note}");
            }
            catch (Exception ex)
            {
                note = $"⚠️ rating saved, but {what} Must Hear failed: {ex.Message}";
                // Log the full exception: the one-line note in the window loses the detail that
                // makes a failure here diagnosable.
                Console.WriteLine($"   ✗ Must Hear {what} failed: {ex}");
            }
        }

        return new RateResult(album, cell, note);
    }

    private async Task<string> AddToMustHearAsync(AlbumEntry album)
    {
        var contents = await NumberedList.ReadAsync(_writer, _mustHearTab);
        if (NumberedList.Contains(contents, album.Title, album.Artist))
            return $"already on “{_mustHearTab}”";

        if (!int.TryParse(album.Year, out int year))
            return $"⚠️ couldn't read the year “{album.Year}”, so it wasn't added to Must Hear";

        int position = await NumberedList.InsertByYearAsync(
            _writer, _mustHearTab, album.Title, album.Artist, year);
        return $"added to “{_mustHearTab}” at #{position}";
    }

    /// <summary>
    /// Takes an album off the Must Hear list and renumbers what's left, so dropping a ⭐ leaves no
    /// gap in the numbering. Matching is <see cref="NumberedList.Matches"/>, so punctuation and
    /// case drifting between the two tabs can't strand an entry that should have gone.
    /// </summary>
    private async Task<string> RemoveFromMustHearAsync(AlbumEntry album)
    {
        var plan = await NumberedList.ApplyAsync(_writer, _mustHearTab,
            r => NumberedList.Matches(r.Title, album.Title)
              && NumberedList.Matches(r.Artist, album.Artist));

        return plan.Removed.Count > 0
            ? $"removed from “{_mustHearTab}”"
            : $"wasn’t on “{_mustHearTab}”";
    }
}
