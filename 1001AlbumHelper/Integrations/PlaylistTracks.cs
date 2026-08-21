using System;
using System.Collections.Generic;
using System.Linq;

namespace _1001AlbumHelper;

/// <summary>One track as read back from an Apple Music playlist, before albums are collapsed.</summary>
public readonly record struct PlaylistTrack(string Album, string Artist);

/// <summary>
/// One track as the Music app's library search reported it, on the way in to a playlist.
/// </summary>
/// <param name="Disc">Disc number, or 0 when the track doesn't say.</param>
/// <param name="Number">Track number within the disc, or 0 when the track doesn't say.</param>
/// <param name="Playable">
/// False for a track Apple Music will no longer play — its cloud status is "no longer available".
/// The track is still in the catalog and still answers a search; it just can't be listened to.
/// </param>
public sealed record LibraryTrack(
    string Album,
    string AlbumArtist,
    string PersistentId,
    int Disc = 0,
    int Number = 0,
    string Name = "",
    bool Playable = true);

/// <summary>Which copy of each song to add, and which songs no copy can play.</summary>
/// <param name="Chosen">One track per slot, in disc/track order.</param>
/// <param name="Unavailable">
/// Slots where every copy is dead. Named rather than counted, so a push can say which songs the
/// album will be short of instead of quietly adding fewer tracks than the album has.
/// </param>
public sealed record TrackSelection(
    IReadOnlyList<LibraryTrack> Chosen,
    IReadOnlyList<LibraryTrack> Unavailable);

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
    /// Picks one copy of each song, preferring one that will actually play.
    /// <para>
    /// Apple Music stocks the same album more than once, and the copies are not always equally
    /// alive: Method Man's <em>Tical 2000: Judgement Day</em> is there twice, and on one of them
    /// five tracks are "no longer available" — greyed out, unplayable — while the other has those
    /// same five. Nothing separates the two: same album name, same artist, same track numbers, same
    /// release date, only the durations differ by a few hundredths of a second. So there is no
    /// "pick the good edition" to be had; the choice has to be made a song at a time, which lands
    /// on a complete, playable album either way.
    /// </para>
    /// <para>
    /// Songs are matched by disc and track number, falling back to the name for tracks that carry
    /// no number. Taking one copy per slot also fixes the plainer bug underneath: two editions
    /// matching one search used to add both, so the album went in twice over.
    /// </para>
    /// </summary>
    public static TrackSelection PreferPlayable(IEnumerable<LibraryTrack> tracks)
    {
        var order = new List<string>();
        var bySlot = new Dictionary<string, LibraryTrack>();

        foreach (var track in tracks)
        {
            // A track with no number is identified by its name — that's all a hidden track, or
            // anything the catalog left unnumbered, has to tell one slot from another.
            string slot = track.Number > 0
                ? $"{track.Disc}/{track.Number}"
                : $"name:{NumberedList.Normalize(track.Name)}";

            if (!bySlot.TryGetValue(slot, out var seen))
            {
                bySlot[slot] = track;
                order.Add(slot);
            }
            else if (!seen.Playable && track.Playable)
            {
                // First playable copy of a slot we'd otherwise have to skip.
                bySlot[slot] = track;
            }
        }

        var picked = order.Select(slot => bySlot[slot]).ToList();
        return new TrackSelection(
            picked.Where(t => t.Playable).ToList(),
            picked.Where(t => !t.Playable).ToList());
    }

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
