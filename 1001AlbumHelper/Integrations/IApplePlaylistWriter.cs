namespace _1001AlbumHelper;

/// <summary>The outcome of a single Apple Music playlist operation.</summary>
/// <param name="OpenUrl">
/// Set on a failure the user can fix by hand in the Music app — an album Apple Music has but the
/// library doesn't — as a link to its page there (see <see cref="AppleMusicCatalog.MusicAppUrl"/>).
/// </param>
public sealed record PlaylistOpResult(bool Ok, string Message, string? OpenUrl = null);

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

    /// <summary>
    /// Whether this platform can read and set Apple Music's album favourites at all. False on the
    /// iPhone: MediaPlayer exposes no favourite (formerly "loved") property, and the framework that
    /// does — MusicKit — is Swift-only, the same wall the missing remove API runs into. The Mac
    /// drives the Music app over AppleScript, where <c>album favorited</c> is read/write.
    /// </summary>
    bool CanFavorite => false;

    /// <summary>
    /// Every album in the library currently marked as a favourite. Throws where
    /// <see cref="CanFavorite"/> is false.
    /// </summary>
    Task<IReadOnlyList<PlaylistEntry>> ReadFavoritedAlbumsAsync() =>
        throw new NotSupportedException("Apple Music favourites can't be read on this platform.");

    /// <summary>
    /// Marks one album as a favourite in Apple Music. Idempotent — favouriting an album that
    /// already is one changes nothing. Throws where <see cref="CanFavorite"/> is false.
    /// </summary>
    Task<PlaylistOpResult> FavoriteAlbumAsync(PlaylistEntry album) =>
        throw new NotSupportedException("Apple Music favourites can't be set on this platform.");
}

/// <summary>
/// Where the mobile UI finds the Apple Music writer. The iOS app sets <see cref="Writer"/> at
/// startup; it stays null on platforms without Apple Music, which the views treat as "not available."
/// </summary>
public static class AppleMusic
{
    public static IApplePlaylistWriter? Writer { get; set; }

    public static bool IsAvailable => Writer is not null;

    /// <summary>Whether favourites can be read and set here — see <see cref="IApplePlaylistWriter.CanFavorite"/>.</summary>
    public static bool CanFavorite => Writer is { CanFavorite: true };

    /// <summary>
    /// The Apple Music playlist that shadows the favourites — a deliberate duplicate of them, kept
    /// because a playlist is somewhere you can actually sit down and play, and because the name is
    /// funny. The backfill fills both together.
    /// </summary>
    public const string FavoritesPlaylistName = "Almost forgot to listen to these today";
}
