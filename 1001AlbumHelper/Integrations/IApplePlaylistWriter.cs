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
/// playlist, so on the phone "remove" happens in the app's own working list, not in Apple Music.
/// The Mac's Music app can delete a track from a playlist, so there <see cref="CanRemove"/> is true.
/// </para>
/// </summary>
public interface IApplePlaylistWriter
{
    /// <summary>Adds one album to the Apple Music playlist named <paramref name="playlistName"/>.</summary>
    Task<PlaylistOpResult> AddAlbumAsync(string playlistName, PlaylistEntry album);

    /// <summary>The albums currently in the Apple Music playlist named <paramref name="playlistName"/>.</summary>
    Task<IReadOnlyList<PlaylistEntry>> ReadAlbumsAsync(string playlistName);

    /// <summary>
    /// Whether an album can be taken back out of an Apple Music playlist from here. False on the
    /// iPhone (MediaPlayer is add-only); true on the Mac.
    /// </summary>
    bool CanRemove => false;

    /// <summary>
    /// Takes one album's tracks out of the playlist named <paramref name="playlistName"/> — the
    /// playlist only; the library keeps them. An album the playlist doesn't hold counts as removed.
    /// Throws where <see cref="CanRemove"/> is false.
    /// </summary>
    Task<PlaylistOpResult> RemoveAlbumAsync(string playlistName, PlaylistEntry album) =>
        throw new NotSupportedException("Albums can't be removed from an Apple Music playlist on this platform.");

    /// <summary>
    /// Whether this platform can read and set Apple Music's album favorites at all. False on the
    /// iPhone: MediaPlayer exposes no favorite (formerly "loved") property, and the framework that
    /// does — MusicKit — is Swift-only, the same wall the missing remove API runs into. The Mac
    /// drives the Music app over AppleScript, where <c>album favorited</c> is read/write.
    /// </summary>
    bool CanFavorite => false;

    /// <summary>
    /// Every album in the library currently marked as a favorite. Throws where
    /// <see cref="CanFavorite"/> is false.
    /// </summary>
    Task<IReadOnlyList<PlaylistEntry>> ReadFavoritedAlbumsAsync() =>
        throw new NotSupportedException("Apple Music favorites can't be read on this platform.");

    /// <summary>
    /// Marks one album as a favorite in Apple Music. Idempotent — favoriting an album that
    /// already is one changes nothing. Throws where <see cref="CanFavorite"/> is false.
    /// </summary>
    Task<PlaylistOpResult> FavoriteAlbumAsync(PlaylistEntry album) =>
        throw new NotSupportedException("Apple Music favorites can't be set on this platform.");
}

/// <summary>
/// Where the mobile UI finds the Apple Music writer. The iOS app sets <see cref="Writer"/> at
/// startup; it stays null on platforms without Apple Music, which the views treat as "not available."
/// </summary>
public static class AppleMusic
{
    public static IApplePlaylistWriter? Writer { get; set; }

    public static bool IsAvailable => Writer is not null;

    /// <summary>Whether albums can be removed from a playlist here — see <see cref="IApplePlaylistWriter.CanRemove"/>.</summary>
    public static bool CanRemove => Writer is { CanRemove: true };

    /// <summary>Whether favorites can be read and set here — see <see cref="IApplePlaylistWriter.CanFavorite"/>.</summary>
    public static bool CanFavorite => Writer is { CanFavorite: true };

    /// <summary>
    /// The Apple Music playlist that shadows the favorites — a deliberate duplicate of them, kept
    /// because a playlist is somewhere you can actually sit down and play, and because the name is
    /// funny. The backfill fills both together.
    /// </summary>
    public const string FavoritesPlaylistName = "Almost forgot to listen to these today";
}
