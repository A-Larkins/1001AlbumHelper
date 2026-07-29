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

    public string Display
    {
        get
        {
            string s = string.IsNullOrEmpty(Year) ? $"{Title} — {Artist}" : $"{Title} — {Artist} ({Year})";
            return TrackCount is > 0 and <= 3 ? $"{s} · only {TrackCount} track{(TrackCount == 1 ? "" : "s")} — check it's not partially deleted" : s;
        }
    }
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
    /// Folds albums read back from the real Apple Music playlist into the working list: adds any
    /// that aren't here yet and marks every matched entry as confirmed-in-Apple-Music (with its
    /// current track count). Returns the number newly added.
    /// </summary>
    public int MergeFromAppleMusic(IEnumerable<PlaylistEntry> albums)
    {
        int added = 0;
        foreach (var album in albums)
        {
            int i = _entries.FindIndex(e => NumberedList.Matches(e.Title, album.Title) && NumberedList.Matches(e.Artist, album.Artist));
            if (i < 0)
            {
                _entries.Add(album with { InAppleMusic = true });
                added++;
            }
            else
            {
                _entries[i] = _entries[i] with { InAppleMusic = true, TrackCount = album.TrackCount };
            }
        }
        Save();
        return added;
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
