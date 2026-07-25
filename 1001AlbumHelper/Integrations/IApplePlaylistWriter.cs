namespace _1001AlbumHelper;

/// <summary>The outcome of pushing a set of albums into an Apple Music playlist.</summary>
public sealed record PlaylistSyncResult(int Added, int NotFound, int Failed, string? Error = null)
{
    public bool IsError => Error is not null;

    public string Summary => IsError
        ? Error!
        : $"Added {Added}" +
          (NotFound > 0 ? $", {NotFound} not found on Apple Music" : "") +
          (Failed > 0 ? $", {Failed} failed" : "") + ".";
}

/// <summary>
/// Adds albums to a named Apple Music library playlist. Implemented per-platform: the iOS head
/// provides a MediaPlayer-backed writer; other platforms have none (the button just reports that).
/// </summary>
public interface IApplePlaylistWriter
{
    Task<PlaylistSyncResult> AddAlbumsAsync(
        string playlistName, IReadOnlyList<PlaylistEntry> albums, IProgress<string>? progress = null);
}

/// <summary>
/// Where the mobile UI finds the Apple Music writer. The iOS app sets <see cref="Writer"/> at
/// startup; it stays null on platforms without Apple Music, which the playlist view treats as
/// "not available here."
/// </summary>
public static class AppleMusic
{
    public static IApplePlaylistWriter? Writer { get; set; }

    public static bool IsAvailable => Writer is not null;
}
