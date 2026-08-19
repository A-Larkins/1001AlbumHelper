using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// An Apple Music playlist holds tracks, but the app's working lists hold albums — so both the
/// iPhone (MediaPlayer) and the Mac (the Music app) fold what they read back through
/// <see cref="PlaylistTracks"/>. These cover that fold, including the track counting the
/// "half-deleted album" warning depends on.
/// </summary>
public class PlaylistTracksTests
{
    private static PlaylistTrack Track(string album, string artist) => new(album, artist);

    [Fact]
    public void Collapses_tracks_of_one_album_into_a_single_entry()
    {
        var albums = PlaylistTracks.CollapseToAlbums(new[]
        {
            Track("Kind of Blue", "Miles Davis"),
            Track("Kind of Blue", "Miles Davis"),
            Track("Kind of Blue", "Miles Davis"),
        });

        var only = Assert.Single(albums);
        Assert.Equal("Kind of Blue", only.Title);
        Assert.Equal("Miles Davis", only.Artist);
        Assert.Equal(3, only.TrackCount);
    }

    [Fact]
    public void Keeps_albums_in_the_order_they_first_appear()
    {
        var albums = PlaylistTracks.CollapseToAlbums(new[]
        {
            Track("Tago Mago", "Can"),
            Track("Sound Affects", "The Jam"),
            Track("Tago Mago", "Can"),
        });

        Assert.Equal(new[] { "Tago Mago", "Sound Affects" }, albums.Select(a => a.Title));
    }

    [Fact]
    public void Treats_names_that_only_differ_by_normalisation_as_the_same_album()
    {
        // "The Beatles" vs "Beatles" is the same artist everywhere else in the app, so a playlist
        // whose tracks disagree about the leading "the" must not split into two albums.
        var albums = PlaylistTracks.CollapseToAlbums(new[]
        {
            Track("Revolver", "The Beatles"),
            Track("revolver", "Beatles"),
        });

        var only = Assert.Single(albums);
        Assert.Equal(2, only.TrackCount);
        Assert.Equal("Revolver", only.Title); // the first spelling seen is the one kept
    }

    [Fact]
    public void Skips_tracks_with_no_album_name()
    {
        var albums = PlaylistTracks.CollapseToAlbums(new[]
        {
            Track("", "Some Artist"),
            Track("   ", "Some Artist"),
            Track("Låtar", "Dungen"),
        });

        Assert.Equal("Låtar", Assert.Single(albums).Title);
    }

    [Fact]
    public void Empty_playlist_folds_to_nothing()
    {
        Assert.Empty(PlaylistTracks.CollapseToAlbums(Array.Empty<PlaylistTrack>()));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Flags_a_short_album_as_possibly_half_deleted(int trackCount, bool expected)
    {
        var albums = PlaylistTracks.CollapseToAlbums(
            Enumerable.Repeat(Track("Pet Sounds", "The Beach Boys"), trackCount));

        Assert.Equal(expected, Assert.Single(albums).IsPartial);
    }

    [Fact]
    public void An_album_never_read_from_apple_music_is_not_flagged()
    {
        // TrackCount stays 0 for an album queued from a list rather than read back — unknown, not short.
        var entry = new PlaylistEntry("Loveless", "My Bloody Valentine", "1991");

        Assert.False(entry.IsPartial);
        Assert.Equal("", entry.PartialWarning);
        Assert.Equal("Loveless — My Bloody Valentine (1991)", entry.Display);
    }

    [Fact]
    public void Display_carries_the_half_deleted_note_for_a_short_album()
    {
        var entry = new PlaylistEntry("Loveless", "My Bloody Valentine", "1991") { TrackCount = 2 };

        Assert.True(entry.IsPartial);
        Assert.Contains("only 2 tracks", entry.Display);
    }
}
