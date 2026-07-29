using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Foundation;
using MediaPlayer;

namespace _1001AlbumHelper;

/// <summary>
/// iOS implementation of <see cref="IApplePlaylistWriter"/> over the MediaPlayer framework.
/// Finds an existing library playlist by name and adds albums to it (by Apple Music store id,
/// looked up via <see cref="AppleMusicCatalog"/>), and reads a playlist's albums back.
///
/// Requires an Apple Music subscription and the media-library permission
/// (NSAppleMusicUsageDescription in Info.plist). MediaPlayer is add-only, so there is no remove.
///
/// NOTE: exercised on-device (the simulator on this toolchain is too unstable to run).
/// </summary>
public sealed class MediaPlayerPlaylistWriter : IApplePlaylistWriter
{
    public async Task<PlaylistOpResult> AddAlbumAsync(string playlistName, PlaylistEntry album)
    {
        if (await RequestAuthorizationAsync() != MPMediaLibraryAuthorizationStatus.Authorized)
            return new PlaylistOpResult(false, "Apple Music access wasn't granted (Settings ▸ Privacy ▸ Media & Apple Music).");

        var playlist = FindPlaylistByName(playlistName);
        if (playlist is null)
            return new PlaylistOpResult(false, $"No Apple Music playlist named “{playlistName}” — create it in Apple Music first.");

        AppleMusicAlbum? match;
        try { match = await AppleMusicCatalog.FindAlbumAsync(album.Artist, album.Title); }
        catch (Exception ex) { return new PlaylistOpResult(false, $"Lookup failed: {ex.Message}"); }

        if (match is null)
            return new PlaylistOpResult(false, $"Not on Apple Music: {album.Title}");

        try
        {
            await AddItemAsync(playlist, match.CollectionId.ToString());
            return new PlaylistOpResult(true, $"Added to {playlistName}");
        }
        catch (Exception ex)
        {
            return new PlaylistOpResult(false, $"Couldn't add: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<PlaylistEntry>> ReadAlbumsAsync(string playlistName)
    {
        if (await RequestAuthorizationAsync() != MPMediaLibraryAuthorizationStatus.Authorized)
            return Array.Empty<PlaylistEntry>();

        var playlist = FindPlaylistByName(playlistName);
        if (playlist is null) return Array.Empty<PlaylistEntry>();

        // One MPMediaItem per track; collapse to one entry per album (title+artist), in first-seen
        // order, counting tracks per album so a half-deleted album (some tracks removed by the user
        // directly in Apple Music) is still visible with its actual remaining count.
        var order = new List<string>();
        var byKey = new Dictionary<string, (string Title, string Artist, int Count)>();
        foreach (var item in playlist.Items ?? Array.Empty<MPMediaItem>())
        {
            string title = item.AlbumTitle ?? "";
            string artist = item.AlbumArtist ?? item.Artist ?? "";
            if (title.Length == 0) continue;

            string key = $"{NumberedList.Normalize(title)}|{NumberedList.Normalize(artist)}";
            if (byKey.TryGetValue(key, out var existing))
                byKey[key] = (existing.Title, existing.Artist, existing.Count + 1);
            else
            {
                byKey[key] = (title, artist, 1);
                order.Add(key);
            }
        }
        return order.Select(key =>
        {
            var (title, artist, count) = byKey[key];
            return new PlaylistEntry(title, artist, "") { TrackCount = count };
        }).ToList();
    }

    // ---- MediaPlayer plumbing (its APIs are callback-based; wrap them as tasks) ----

    private MPMediaPlaylist? FindPlaylistByName(string name)
    {
        var query = MPMediaQuery.PlaylistsQuery;
        foreach (var collection in query.Collections ?? Array.Empty<MPMediaItemCollection>())
        {
            if (collection is MPMediaPlaylist playlist
                && string.Equals(playlist.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return playlist;
            }
        }
        return null;
    }

    private static Task<MPMediaLibraryAuthorizationStatus> RequestAuthorizationAsync()
    {
        var tcs = new TaskCompletionSource<MPMediaLibraryAuthorizationStatus>();
        MPMediaLibrary.RequestAuthorization(status => tcs.TrySetResult(status));
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
}
