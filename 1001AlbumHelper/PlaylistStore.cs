using System.Text.Json;

namespace _1001AlbumHelper;

/// <summary>One album queued for an Apple Music playlist.</summary>
public sealed record PlaylistEntry(string Title, string Artist, string Year)
{
    public string Display => string.IsNullOrEmpty(Year)
        ? $"{Title} — {Artist}"
        : $"{Title} — {Artist} ({Year})";
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

    public bool Remove(PlaylistEntry entry)
    {
        bool removed = _entries.Remove(entry);
        if (removed) Save();
        return removed;
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
