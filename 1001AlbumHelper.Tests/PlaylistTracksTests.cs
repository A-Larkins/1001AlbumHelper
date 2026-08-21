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

    // ---------- Choosing which copy of a song to add ----------
    //
    // Apple Music stocks some albums twice under identical names, and the copies aren't equally
    // alive. Method Man's Tical 2000: Judgement Day is the case that surfaced it: two listings,
    // same name, same artist, same track numbers, same release date — and five tracks withdrawn on
    // one of them that play perfectly on the other.

    private static LibraryTrack Track(int number, string name, bool playable = true, string id = "") =>
        new("Tical 2000: Judgement Day", "Method Man", id.Length > 0 ? id : $"{number}-{playable}",
            Disc: 1, Number: number, Name: name, Playable: playable);

    [Fact]
    public void A_withdrawn_track_gives_way_to_the_copy_that_plays()
    {
        var selection = PlaylistTracks.PreferPlayable(new[]
        {
            Track(11, "Retro Godfather", playable: false, id: "dead"),
            Track(11, "Retro Godfather", playable: true, id: "alive"),
        });

        Assert.Equal("alive", Assert.Single(selection.Chosen).PersistentId);
        Assert.Empty(selection.Unavailable);
    }

    [Fact]
    public void The_playable_copy_wins_whichever_order_the_search_returned_them_in()
    {
        var selection = PlaylistTracks.PreferPlayable(new[]
        {
            Track(11, "Retro Godfather", playable: true, id: "alive"),
            Track(11, "Retro Godfather", playable: false, id: "dead"),
        });

        Assert.Equal("alive", Assert.Single(selection.Chosen).PersistentId);
    }

    [Fact]
    public void Two_listings_of_one_album_add_up_to_one_playable_album()
    {
        // The real shape, in miniature: four songs, one edition missing two of them.
        var withdrawn = new[] { Track(2, "Sweet Love (Skit)", false), Track(3, "Retro Godfather", false) };
        var alive = new[] { Track(2, "Sweet Love (Skit)"), Track(3, "Retro Godfather") };
        var shared = new[] { Track(1, "Judgement Day (Intro)"), Track(4, "Torture") };

        var selection = PlaylistTracks.PreferPlayable(shared.Concat(withdrawn).Concat(alive));

        Assert.Equal(4, selection.Chosen.Count);              // not eight, and not two short
        Assert.All(selection.Chosen, t => Assert.True(t.Playable));
        Assert.Empty(selection.Unavailable);
    }

    [Fact]
    public void An_album_listed_twice_is_not_added_twice_over()
    {
        var once = new[] { Track(1, "Judgement Day (Intro)"), Track(2, "Torture") };

        var selection = PlaylistTracks.PreferPlayable(once.Concat(once));

        Assert.Equal(2, selection.Chosen.Count);
    }

    [Fact]
    public void A_song_no_copy_can_play_is_named_rather_than_quietly_dropped()
    {
        var selection = PlaylistTracks.PreferPlayable(new[]
        {
            Track(1, "Judgement Day (Intro)"),
            Track(2, "Sweet Love (Skit)", playable: false),
            Track(2, "Sweet Love (Skit)", playable: false),
        });

        Assert.Single(selection.Chosen);
        Assert.Equal("Sweet Love (Skit)", Assert.Single(selection.Unavailable).Name);
    }

    [Fact]
    public void Songs_keep_the_order_the_album_puts_them_in()
    {
        var selection = PlaylistTracks.PreferPlayable(new[]
        {
            Track(1, "Judgement Day (Intro)"),
            Track(2, "Torture"),
            Track(3, "Perfect World"),
        });

        Assert.Equal(new[] { 1, 2, 3 }, selection.Chosen.Select(t => t.Number));
    }

    [Fact]
    public void The_same_number_on_a_different_disc_is_a_different_song()
    {
        var selection = PlaylistTracks.PreferPlayable(new[]
        {
            new LibraryTrack("Sandinista!", "The Clash", "d1t1", Disc: 1, Number: 1, Name: "The Magnificent Seven"),
            new LibraryTrack("Sandinista!", "The Clash", "d2t1", Disc: 2, Number: 1, Name: "Lightning Strikes"),
        });

        Assert.Equal(2, selection.Chosen.Count);
    }

    [Fact]
    public void Unnumbered_tracks_fall_back_to_their_names()
    {
        var selection = PlaylistTracks.PreferPlayable(new[]
        {
            new LibraryTrack("Album", "Artist", "a", Name: "Hidden Track", Playable: false),
            new LibraryTrack("Album", "Artist", "b", Name: "Hidden Track"),
            new LibraryTrack("Album", "Artist", "c", Name: "Another"),
        });

        Assert.Equal(2, selection.Chosen.Count);
        Assert.Equal("b", selection.Chosen[0].PersistentId);
    }
}
