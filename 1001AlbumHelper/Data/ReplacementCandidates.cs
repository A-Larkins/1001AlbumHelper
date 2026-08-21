using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _1001AlbumHelper;

/// <summary>Where a candidate has got to. Only <see cref="Pending"/> ones are offered.</summary>
public enum CandidateStatus
{
    Pending,
    Added,
    Declined,
}

/// <summary>
/// One album being considered for the replacements list.
/// <para>
/// Mutable and change-notifying rather than a record, because the year is edited in place in the
/// list and the note updates as an add is attempted.
/// </para>
/// </summary>
public sealed class CandidateAlbum : INotifyPropertyChanged
{
    private string _year = "";
    private string _note = "";

    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Genre { get; set; } = "";
    public CandidateStatus Status { get; set; } = CandidateStatus.Pending;

    /// <summary>Blank until looked up on Discogs or typed in — the seed list has no years.</summary>
    public string Year
    {
        get => _year;
        set => Set(ref _year, value ?? "");
    }

    /// <summary>Per-row feedback ("Discogs says 1994…", "already on the 1001 list"). Not persisted.</summary>
    [JsonIgnore]
    public string Note
    {
        get => _note;
        set => Set(ref _note, value ?? "");
    }

    [JsonIgnore]
    public string Display => $"{Title} — {Artist}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(Note)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNote)));
    }

    [JsonIgnore]
    public bool HasNote => _note.Length > 0;

    [JsonIgnore]
    public bool HasGenre => Genre.Length > 0;
}

/// <summary>Which column the shortlist is ordered by when the user sorts it.</summary>
public enum CandidateSortColumn
{
    Title,
    Artist,
    Genre,
    Year,
}

/// <summary>What adding an album to the shortlist would mean, given what's already on it.</summary>
public enum CandidateAddOutcome
{
    /// <summary>Not on the shortlist — append it.</summary>
    New,

    /// <summary>Already there and still undecided, so adding it again would just duplicate a row.</summary>
    AlreadyPending,

    /// <summary>Already kept, so it's on the replacements list and nothing is owed.</summary>
    AlreadyKept,

    /// <summary>Dropped before and now wanted back — a decision reversed, not a new album.</summary>
    Reopen,
}

/// <summary>
/// The local shortlist of albums that might earn a place on the replacements list, stored as JSON
/// beside the app's other data.
/// <para>
/// Declined and added albums are kept in the file rather than deleted, so a candidate that's been
/// ruled on once never reappears — the file is the record of what's been decided, not just what's
/// left to decide.
/// </para>
/// </summary>
public static class ReplacementCandidates
{
    public const string FileName = "replacement-candidates.json";

    public static string FilePath => Path.Combine(Operations.ProjectDir, FileName);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Works out what adding <paramref name="title"/> by <paramref name="artist"/> would mean.
    /// <para>
    /// Matching is the same loose comparison the sheet uses, so punctuation and a leading "the"
    /// don't let a second row for the same album through. The album is returned alongside the
    /// outcome whenever one was matched, since the caller needs it to act or to name it.
    /// </para>
    /// </summary>
    public static (CandidateAddOutcome Outcome, CandidateAlbum? Match) Classify(
        IEnumerable<CandidateAlbum> shortlist, string title, string artist) =>
        Classify(shortlist, a => NumberedList.Matches(a.Title, title) && NumberedList.Matches(a.Artist, artist));

    /// <summary>
    /// The same question asked with the caller's own idea of when two rows are the same album —
    /// for albums arriving from somewhere that names its own pressing, where a title has to be
    /// matched more loosely than the sheet's rule allows (see <see cref="ShortlistIntake"/>).
    /// </summary>
    public static (CandidateAddOutcome Outcome, CandidateAlbum? Match) Classify(
        IEnumerable<CandidateAlbum> shortlist, Func<CandidateAlbum, bool> isSameAlbum)
    {
        var match = shortlist.FirstOrDefault(isSameAlbum);

        if (match is null) return (CandidateAddOutcome.New, null);

        return match.Status switch
        {
            CandidateStatus.Pending => (CandidateAddOutcome.AlreadyPending, match),
            CandidateStatus.Added => (CandidateAddOutcome.AlreadyKept, match),
            _ => (CandidateAddOutcome.Reopen, match),
        };
    }

    /// <summary>
    /// Folds <paramref name="mine"/> into <paramref name="existing"/> — the shared list as it stands
    /// — and returns the result, so a device writing its changes can't delete what it hasn't heard of.
    /// <para>
    /// Nothing in the app ever removes a candidate: rows are appended, and a decision is a change of
    /// <see cref="CandidateAlbum.Status"/> on a row that stays. A write that made the list shorter is
    /// therefore always a stale copy overwriting a newer one — which is exactly what happened when a
    /// screen loaded the shortlist, a Playlist 2 pull added to it elsewhere, and the screen then saved
    /// the list it had been holding all along. So the merged list only ever grows.
    /// </para>
    /// <para>
    /// Where both sides know an album, the writer's row wins: they're the one making the change, and
    /// a decision has to be able to travel — including an undo, which walks a candidate back from
    /// dropped to pending. A year or genre is the exception, taken from whichever side has one, since
    /// a blank there is an absence rather than an edit and shouldn't erase a value someone else found.
    /// </para>
    /// </summary>
    public static List<CandidateAlbum> Merge(
        IEnumerable<CandidateAlbum> existing, IEnumerable<CandidateAlbum> mine)
    {
        var merged = existing.ToList();

        foreach (var album in mine)
        {
            int i = merged.FindIndex(e =>
                NumberedList.Matches(e.Title, album.Title) && NumberedList.Matches(e.Artist, album.Artist));

            if (i < 0)
            {
                merged.Add(album);
                continue;
            }

            var theirs = merged[i];
            merged[i] = new CandidateAlbum
            {
                Title = album.Title,
                Artist = album.Artist,
                Status = album.Status,
                Year = album.Year.Trim().Length > 0 ? album.Year : theirs.Year,
                Genre = album.Genre.Trim().Length > 0 ? album.Genre : theirs.Genre,
            };
        }

        return merged;
    }

    /// <summary>
    /// Orders the shortlist by one column for display, leaving the underlying list untouched.
    /// <para>
    /// Titles and artists sort the way the sheet compares them — case, punctuation and a leading
    /// "the" set aside — so "The Band" files under B. A blank cell always trails, whichever way the
    /// column runs: an absent year or genre isn't small or large, it's just missing, and it's least
    /// in the way at the bottom. The sort is stable, so rows that tie keep their file order and
    /// re-sorting the same column doesn't reshuffle them.
    /// </para>
    /// </summary>
    public static List<CandidateAlbum> Sort(
        IEnumerable<CandidateAlbum> albums, CandidateSortColumn column, bool descending)
    {
        if (column == CandidateSortColumn.Year)
        {
            var keyed = albums.Select(a => (album: a, has: int.TryParse(a.Year.Trim(), out int y), year: y));
            var byPresence = keyed.OrderBy(x => !x.has); // rows without a year sink to the bottom
            var ordered = descending
                ? byPresence.ThenByDescending(x => x.year)
                : byPresence.ThenBy(x => x.year);
            return ordered.Select(x => x.album).ToList();
        }
        else
        {
            var keyed = albums.Select(a => (album: a, key: SortKey(a, column)));
            var byPresence = keyed.OrderBy(x => x.key.Length == 0); // blanks sink to the bottom
            var ordered = descending
                ? byPresence.ThenByDescending(x => x.key, StringComparer.Ordinal)
                : byPresence.ThenBy(x => x.key, StringComparer.Ordinal);
            return ordered.Select(x => x.album).ToList();
        }
    }

    private static string SortKey(CandidateAlbum a, CandidateSortColumn column) => column switch
    {
        CandidateSortColumn.Title => NumberedList.Normalize(a.Title),
        CandidateSortColumn.Artist => NumberedList.Normalize(a.Artist),
        CandidateSortColumn.Genre => NumberedList.Normalize(a.Genre),
        _ => "",
    };

    /// <summary>Everything in the file, in file order. Returns an empty list when there's no file.</summary>
    public static List<CandidateAlbum> Load() => Load(FilePath);

    /// <inheritdoc cref="Load()"/>
    public static List<CandidateAlbum> Load(string path)
    {
        if (!File.Exists(path)) return new List<CandidateAlbum>();
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Deserializes the shortlist from JSON text (used to load the embedded mobile snapshot).</summary>
    public static List<CandidateAlbum> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<CandidateAlbum>();
        return JsonSerializer.Deserialize<List<CandidateAlbum>>(json, Options) ?? new List<CandidateAlbum>();
    }

    /// <summary>
    /// Writes via a temporary file and a move, so an interrupted save can't leave the shortlist
    /// half-written — this file is the only copy of which albums have been ruled out.
    /// </summary>
    public static void Save(IEnumerable<CandidateAlbum> albums) => Save(FilePath, albums);

    /// <inheritdoc cref="Save(IEnumerable{CandidateAlbum})"/>
    public static void Save(string path, IEnumerable<CandidateAlbum> albums)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(albums.ToList(), Options));
        File.Move(temp, path, overwrite: true);
    }
}
