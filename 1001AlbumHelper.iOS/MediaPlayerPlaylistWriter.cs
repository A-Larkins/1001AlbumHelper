using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Foundation;
using MediaPlayer;

namespace _1001AlbumHelper;

/// <summary>
/// iOS implementation of <see cref="IApplePlaylistWriter"/>. Finds each album in the Apple Music
/// catalogue (via <see cref="AppleMusicCatalog"/>) and adds it, by store id, to a library playlist
/// created/reused under a name-derived UUID so re-syncing tops up the same playlist.
///
/// Requires an Apple Music subscription and the media-library permission
/// (NSAppleMusicUsageDescription in Info.plist). Uses the MediaPlayer framework's MPMediaLibrary,
/// which does NOT need the paid MusicKit entitlement.
///
/// NOTE: written against the MediaPlayer API but not yet exercised on a device — the simulator on
/// this toolchain is too unstable to run. First real run is the device deploy; adjust here if the
/// add-by-album-id behaviour needs to become add-by-track-id.
/// </summary>
public sealed class MediaPlayerPlaylistWriter : IApplePlaylistWriter
{
    public async Task<PlaylistSyncResult> AddAlbumsAsync(
        string playlistName, IReadOnlyList<PlaylistEntry> albums, IProgress<string>? progress = null)
    {
        var status = await RequestAuthorizationAsync();
        if (status != MPMediaLibraryAuthorizationStatus.Authorized)
            return new PlaylistSyncResult(0, 0, 0,
                "Apple Music access wasn't granted. Enable it in Settings › Privacy › Media & Apple Music.");

        MPMediaPlaylist playlist;
        try
        {
            playlist = await GetOrCreatePlaylistAsync(playlistName);
        }
        catch (Exception ex)
        {
            return new PlaylistSyncResult(0, 0, 0, $"Couldn't open the Apple Music playlist: {ex.Message}");
        }

        int added = 0, notFound = 0, failed = 0;
        foreach (var album in albums)
        {
            progress?.Report($"Adding {album.Title}…");

            AppleMusicAlbum? match;
            try { match = await AppleMusicCatalog.FindAlbumAsync(album.Artist, album.Title); }
            catch { match = null; }

            if (match is null) { notFound++; continue; }

            try
            {
                await AddItemAsync(playlist, match.CollectionId.ToString());
                added++;
            }
            catch { failed++; }
        }

        return new PlaylistSyncResult(added, notFound, failed);
    }

    // MediaPlayer's APIs are callback-based; wrap them as tasks.

    private static Task<MPMediaLibraryAuthorizationStatus> RequestAuthorizationAsync()
    {
        var tcs = new TaskCompletionSource<MPMediaLibraryAuthorizationStatus>();
        MPMediaLibrary.RequestAuthorization(status => tcs.TrySetResult(status));
        return tcs.Task;
    }

    private static Task<MPMediaPlaylist> GetOrCreatePlaylistAsync(string name)
    {
        var tcs = new TaskCompletionSource<MPMediaPlaylist>();
        var metadata = new MPMediaPlaylistCreationMetadata(name);
        MPMediaLibrary.DefaultMediaLibrary.GetPlaylist(StableUuid(name), metadata, (playlist, error) =>
        {
            if (error is not null) tcs.TrySetException(new Exception(error.LocalizedDescription));
            else tcs.TrySetResult(playlist);
        });
        return tcs.Task;
    }

    private static Task AddItemAsync(MPMediaPlaylist playlist, string productId)
    {
        var tcs = new TaskCompletionSource<bool>();
        playlist.AddItem(productId, error =>
        {
            if (error is not null) tcs.TrySetException(new Exception(error.LocalizedDescription));
            else tcs.TrySetResult(true);
        });
        return tcs.Task;
    }

    /// <summary>A stable UUID per playlist name, so the same library playlist is reused each sync.</summary>
    private static NSUuid StableUuid(string name)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("1001albums:" + name));
        return new NSUuid(new Guid(hash).ToString());
    }
}
