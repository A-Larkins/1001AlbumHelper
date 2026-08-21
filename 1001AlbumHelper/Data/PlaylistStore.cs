using System.Linq;
using System.Text.Json;

namespace _1001AlbumHelper;

/// <summary>One album queued for an Apple Music playlist.</summary>
public sealed record PlaylistEntry(string Title, string Artist, string Year)
{
    /// <summary>True once we know this album is actually in the Apple Music playlist (pushed or imported).</summary>
    public bool InAppleMusic { get; init; }

    /// <summary>
    /// True when the user removed this from the working list but it's still in Apple Music — Apple's
    /// API can't remove it for us, so it stays visible as a "delete this yourself" reminder until
    /// they confirm they've done so.
    /// </summary>
    public bool PendingRemoval { get; init; }

    /// <summary>Track count from the last Apple Music read, or 0 if unknown — a low count can mean a half-deleted album.</summary>
    public int TrackCount { get; init; }

    /// <summary>
    /// True when Apple Music reported suspiciously few tracks for this album — the usual cause is
    /// the user having deleted some of them by hand, leaving a stump behind.
    /// </summary>
    public bool IsPartial => TrackCount is > 0 and <= 3;

    /// <summary>The half-deleted note on its own, for layouts that show it beside the title.</summary>
    public string PartialWarning => IsPartial
        ? $"only {TrackCount} track{(TrackCount == 1 ? "" : "s")} — check it's not partially deleted"
        : "";

    public string Display
    {
        get
        {
            string s = string.IsNullOrEmpty(Year) ? $"{Title} — {Artist}" : $"{Title} — {Artist} ({Year})";
            return IsPartial ? $"{s} · {PartialWarning}" : s;
        }
    }
}

/// <summary>What a pull-down sync changed, so the UI can say more than "done".</summary>
/// <param name="NewAlbums">
/// Albums Apple Music had that the working list didn't — named rather than counted, because these
/// are the ones the user queued in Apple Music by hand, and Playlist 2's pull feeds them on to the
/// potentials shortlist (see <see cref="ShortlistIntake"/>).
/// </param>
/// <param name="Removed">Albums the working list had that Apple Music didn't — dropped.</param>
/// <param name="ClearedFromChecklist">Albums awaiting manual deletion that have now gone from Apple Music.</param>
public sealed record PlaylistSyncResult(
    IReadOnlyList<PlaylistEntry> NewAlbums, int Removed, int ClearedFromChecklist)
{
    /// <summary>How many albums the pull brought in.</summary>
    public int Added => NewAlbums.Count;
}

/// <summary>
/// One of the two on-device playlists the mobile app builds up as the user works through the
/// lists:
///   1 = albums from the original 1001 run-through,
///   2 = potential recommended replacements.
/// Persisted as JSON in the app's writable data folder. This is the local record of what the
/// user has picked; pushing those albums into Apple Music proper is layered on top of it.
/// </summary>
public sealed class PlaylistStore
{
    public int Id { get; }
    private readonly string _path;
    private readonly List<PlaylistEntry> _entries;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private PlaylistStore(int id, string path, List<PlaylistEntry> entries)
    {
        Id = id;
        _path = path;
        _entries = entries;
    }

    public IReadOnlyList<PlaylistEntry> Entries => _entries;

    /// <summary>The working list, minus anything awaiting manual deletion from Apple Music.</summary>
    public IReadOnlyList<PlaylistEntry> Active => _entries.Where(e => !e.PendingRemoval).ToList();

    /// <summary>Albums removed from the working list that are still in Apple Music and need deleting by hand.</summary>
    public IReadOnlyList<PlaylistEntry> ToRemove => _entries.Where(e => e.PendingRemoval).ToList();

    /// <summary>The app's writable data folder (the iOS sandbox on device).</summary>
    public static string DataDir
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppContext.BaseDirectory;
            var dir = Path.Combine(baseDir, "1001AlbumHelper");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Loads playlist <paramref name="id"/> from disk (empty if it doesn't exist yet).</summary>
    public static PlaylistStore Open(int id)
    {
        var path = Path.Combine(DataDir, $"playlist{id}.json");
        var entries = new List<PlaylistEntry>();
        if (File.Exists(path))
        {
            try
            {
                entries = JsonSerializer.Deserialize<List<PlaylistEntry>>(File.ReadAllText(path), Options)
                          ?? new List<PlaylistEntry>();
            }
            catch { entries = new List<PlaylistEntry>(); } // a corrupt file shouldn't brick the tab
        }
        return new PlaylistStore(id, path, entries);
    }

    public bool Contains(string title, string artist) =>
        _entries.Any(e => NumberedList.Matches(e.Title, title) && NumberedList.Matches(e.Artist, artist));

    /// <summary>Adds the album unless it's already present. Returns true if it was newly added.</summary>
    public bool Add(string title, string artist, string year)
    {
        if (Contains(title, artist)) return false;
        _entries.Add(new PlaylistEntry(title, artist, year));
        Save();
        return true;
    }

    /// <summary>
    /// The user wants this off the working list. If Apple Music doesn't have it (never pushed or
    /// imported), it's simply deleted. If Apple Music does have it, there's no API to remove it there,
    /// so it moves to <see cref="ToRemove"/> as a reminder until <see cref="ConfirmRemoved"/> is called.
    /// </summary>
    public bool RequestRemoval(PlaylistEntry entry)
    {
        int i = _entries.IndexOf(entry);
        if (i < 0) return false;

        if (entry.InAppleMusic) _entries[i] = entry with { PendingRemoval = true };
        else _entries.RemoveAt(i);
        Save();
        return true;
    }

    /// <summary>The user has manually deleted this album from Apple Music — drop it from the checklist for good.</summary>
    public bool ConfirmRemoved(PlaylistEntry entry)
    {
        bool removed = _entries.Remove(entry);
        if (removed) Save();
        return removed;
    }

    /// <summary>Marks an existing entry as confirmed present in Apple Music (after a successful push).</summary>
    public void MarkInAppleMusic(PlaylistEntry entry)
    {
        int i = _entries.IndexOf(entry);
        if (i < 0 || _entries[i].InAppleMusic) return;
        _entries[i] = _entries[i] with { InAppleMusic = true };
        Save();
    }

    /// <summary>
    /// Whether two entries name the same record. Titles are compared loosely — the same rule the
    /// push path matches on. Apple Music names an album by whichever edition it stocks, so a strict
    /// comparison would read the catalog's "Tago Mago (2011 Remastered)" as a different record from
    /// our "Tago Mago", and every sync would drop ours, re-add theirs, and lose the year in the process.
    /// </summary>
    public static bool SameAlbum(PlaylistEntry a, PlaylistEntry b) =>
        DiscogsApiClient.TitlesLineUp(a.Title, b.Title) && NumberedList.Matches(a.Artist, b.Artist);

    /// <summary>
    /// Pulls the working list down from Apple Music, making that playlist the source of truth:
    /// afterwards the list holds exactly what Apple Music holds, in its order. Albums queued here
    /// but never pushed are dropped — that's what a pull-down means.
    ///
    /// <para>
    /// Two things deliberately survive the wipe. Albums awaiting manual deletion stay on the
    /// checklist rather than reappearing as active — Apple Music still has them, which is the whole
    /// reason they're on it, so repopulating from Apple Music would otherwise undo the user's
    /// removal every time they synced. And where an album is already known here, its
    /// <see cref="PlaylistEntry.Year"/> is carried across, because Apple Music's read-back doesn't
    /// report a year and our lists do.
    /// </para>
    ///
    /// <para>
    /// The flip side is a nice one: an album on the checklist that is <em>gone</em> from Apple Music
    /// means the user has now deleted it by hand, so the sync ticks it off for them.
    /// </para>
    /// </summary>
    public PlaylistSyncResult SyncFromAppleMusic(IEnumerable<PlaylistEntry> albums)
    {
        var incoming = albums.ToList();
        var before = _entries.ToList();

        PlaylistEntry? Existing(PlaylistEntry album) => before.FirstOrDefault(e => SameAlbum(e, album));

        bool StillInAppleMusic(PlaylistEntry entry) => incoming.Any(a => SameAlbum(a, entry));

        // Kept on the checklist only while Apple Music still has them; the rest have been dealt with.
        var pending = before.Where(e => e.PendingRemoval).ToList();
        var stillPending = pending.Where(StillInAppleMusic).ToList();
        int cleared = pending.Count - stillPending.Count;

        var rebuilt = new List<PlaylistEntry>();
        var arrived = new List<PlaylistEntry>();
        foreach (var album in incoming)
        {
            var existing = Existing(album);

            // Anything the user has already taken off the list stays off it, not resurrected here.
            if (existing?.PendingRemoval == true) continue;

            if (existing is null)
            {
                var arrival = album with { InAppleMusic = true };
                rebuilt.Add(arrival);
                arrived.Add(arrival);
            }
            else
            {
                // Keep the name and year we already hold — ours read better than the catalog's
                // ("Tago Mago" rather than "Tago Mago (2011 Remastered)") — but take Apple Music's
                // current track count, which is what flags a half-deleted album.
                rebuilt.Add(existing with { InAppleMusic = true, TrackCount = album.TrackCount });
            }
        }

        // Whatever was on the working list and isn't in Apple Music has just been dropped.
        int removed = before.Count(e => !e.PendingRemoval && !StillInAppleMusic(e));

        rebuilt.AddRange(stillPending);

        _entries.Clear();
        _entries.AddRange(rebuilt);
        Save();

        return new PlaylistSyncResult(arrived, removed, cleared);
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
