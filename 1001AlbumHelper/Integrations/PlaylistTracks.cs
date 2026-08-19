using System;
using System.Collections.Generic;
using System.Linq;

namespace _1001AlbumHelper;

/// <summary>One track as read back from an Apple Music playlist, before albums are collapsed.</summary>
public readonly record struct PlaylistTrack(string Album, string Artist);

/// <summary>
/// Turns the flat track list an Apple Music playlist actually contains into one
/// <see cref="PlaylistEntry"/> per album. Both platforms need exactly this: iOS reads
/// MPMediaItems, macOS reads Music.app tracks, and either way a playlist is tracks, not albums.
/// <para>
/// Track counts are kept, because a low one means the user has half-deleted an album by hand in
/// Apple Music and the playlist tab should say so (see <see cref="PlaylistEntry.Display"/>).
/// </para>
/// </summary>
public static class PlaylistTracks
{
    /// <summary>
    /// Collapses tracks to albums in first-seen order, matching on the same normalised title+artist
    /// the rest of the app compares by, so "The Beatles" and "Beatles" don't split into two albums.
    /// Tracks with no album title are skipped — they can't be matched to anything in our lists.
    /// </summary>
    public static IReadOnlyList<PlaylistEntry> CollapseToAlbums(IEnumerable<PlaylistTrack> tracks)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, (string Title, string Artist, int Count)>();

        foreach (var track in tracks)
        {
            string title = track.Album ?? "";
            string artist = track.Artist ?? "";
            if (title.Trim().Length == 0) continue;

            string key = $"{NumberedList.Normalize(title)}|{NumberedList.Normalize(artist)}";
            if (byKey.TryGetValue(key, out var seen))
            {
                byKey[key] = (seen.Title, seen.Artist, seen.Count + 1);
            }
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
}
