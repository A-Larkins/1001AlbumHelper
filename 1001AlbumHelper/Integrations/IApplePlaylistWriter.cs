namespace _1001AlbumHelper;

/// <summary>The outcome of a single Apple Music playlist operation.</summary>
public sealed record PlaylistOpResult(bool Ok, string Message);

/// <summary>
/// Talks to the user's Apple Music library: adds an album to an existing playlist (found by name),
/// and reads a playlist's albums back. Implemented per-platform — the iOS head provides a
/// MediaPlayer-backed writer; other platforms have none (the mobile views guard on availability).
/// <para>
/// Note: Apple's MediaPlayer framework is add-only — there is no API to remove items from a
/// playlist, so "remove" happens in the app's own working list, not in Apple Music.
/// </para>
/// </summary>
public interface IApplePlaylistWriter
{
    /// <summary>Adds one album to the Apple Music playlist named <paramref name="playlistName"/>.</summary>
    Task<PlaylistOpResult> AddAlbumAsync(string playlistName, PlaylistEntry album);

    /// <summary>The albums currently in the Apple Music playlist named <paramref name="playlistName"/>.</summary>
    Task<IReadOnlyList<PlaylistEntry>> ReadAlbumsAsync(string playlistName);
}

/// <summary>
/// Where the mobile UI finds the Apple Music writer. The iOS app sets <see cref="Writer"/> at
/// startup; it stays null on platforms without Apple Music, which the views treat as "not available."
/// </summary>
public static class AppleMusic
{
    public static IApplePlaylistWriter? Writer { get; set; }

    public static bool IsAvailable => Writer is not null;
}
