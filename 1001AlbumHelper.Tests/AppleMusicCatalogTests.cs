using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The catalogue lookup turns an album into the Apple Music store id a playlist is built from, so
/// these guard the two pure steps: reading the iTunes Search response, and choosing the right row.
/// </summary>
public class AppleMusicCatalogTests
{
    // A trimmed but real-shaped iTunes Search API response.
    private const string SampleJson = """
    {
      "resultCount": 3,
      "results": [
        { "collectionType": "Album", "collectionId": 1440783617, "artistName": "Nirvana", "collectionName": "Nevermind (Remastered)", "trackCount": 13 },
        { "collectionType": "Album", "collectionId": 1631384034, "artistName": "Nirvana", "collectionName": "In Utero - 30th Anniversary", "trackCount": 39 },
        { "wrapperType": "artist", "artistName": "Nirvana" }
      ]
    }
    """;

    [Fact]
    public void Parsing_keeps_only_rows_with_a_collection_id()
    {
        var albums = AppleMusicCatalog.ParseSearchResults(SampleJson);

        Assert.Equal(2, albums.Count); // the artist row (no collectionId) is dropped
        Assert.Equal(1440783617, albums[0].CollectionId);
        Assert.Equal("Nevermind (Remastered)", albums[0].CollectionName);
        Assert.Equal("Nirvana", albums[0].ArtistName);
        Assert.Equal(13, albums[0].TrackCount);
    }

    [Fact]
    public void A_missing_trackCount_is_treated_as_worst_not_smallest()
    {
        var albums = AppleMusicCatalog.ParseSearchResults(
            """{ "results": [{ "collectionType": "Album", "collectionId": 1, "artistName": "X", "collectionName": "Y" }] }""");

        Assert.Equal(int.MaxValue, albums[0].TrackCount);
    }

    [Fact]
    public void Parsing_a_response_with_no_results_is_empty_not_a_crash()
    {
        Assert.Empty(AppleMusicCatalog.ParseSearchResults("""{ "resultCount": 0, "results": [] }"""));
    }

    [Fact]
    public void A_deluxe_or_remastered_edition_still_matches_the_plain_album()
    {
        var albums = AppleMusicCatalog.ParseSearchResults(SampleJson);

        var match = AppleMusicCatalog.FindBestMatch(albums, "Nirvana", "Nevermind");

        Assert.NotNull(match);
        Assert.Equal(1440783617, match!.CollectionId);
    }

    [Fact]
    public void The_artist_disambiguates_between_two_albums_by_the_same_act()
    {
        var albums = AppleMusicCatalog.ParseSearchResults(SampleJson);

        var match = AppleMusicCatalog.FindBestMatch(albums, "Nirvana", "In Utero");

        Assert.NotNull(match);
        Assert.Equal(1631384034, match!.CollectionId);
    }

    [Fact]
    public void Among_several_editions_of_the_same_album_the_smallest_track_count_wins()
    {
        // A deluxe reissue padded with demos/remixes should lose to the plain original release,
        // even though both line up on title and artist equally.
        var albums = AppleMusicCatalog.ParseSearchResults("""
        {
          "results": [
            { "collectionType": "Album", "collectionId": 111, "artistName": "Snoop Doggy Dogg", "collectionName": "Doggystyle (Deluxe Edition)", "trackCount": 25 },
            { "collectionType": "Album", "collectionId": 222, "artistName": "Snoop Doggy Dogg", "collectionName": "Doggystyle", "trackCount": 13 }
          ]
        }
        """);

        var match = AppleMusicCatalog.FindBestMatch(albums, "Snoop Doggy Dogg", "Doggystyle");

        Assert.NotNull(match);
        Assert.Equal(222, match!.CollectionId);
    }

    [Fact]
    public void No_candidates_yields_no_match()
    {
        Assert.Null(AppleMusicCatalog.FindBestMatch(new(), "Nirvana", "Nevermind"));
    }

    // ----- The "nothing lines up" case -----
    // iTunes's relevance ranking for a combined artist+title /search can still rank an unrelated album
    // by the same artist above the real match (e.g. a compilation outranking the actual studio album),
    // so an unmatched top hit is never a safe guess — it must refuse to pick anything instead.

    [Fact]
    public void An_unmatched_title_refuses_to_guess_from_the_top_hit()
    {
        var albums = AppleMusicCatalog.ParseSearchResults(SampleJson);

        var match = AppleMusicCatalog.FindBestMatch(albums, "Nirvana", "Bleach");

        Assert.Null(match);
    }

    // ----- Cover art -----
    // The rating card's artwork comes from the same lookup, so the art shown is the art of the
    // album we'd actually add to a playlist.

    [Fact]
    public void Parsing_keeps_the_cover_art_url()
    {
        var albums = AppleMusicCatalog.ParseSearchResults("""
        {
          "results": [
            { "collectionId": 1, "artistName": "Nirvana", "collectionName": "Nevermind", "trackCount": 13,
              "artworkUrl100": "https://is1-ssl.mzstatic.com/image/thumb/Music/abc/source/100x100bb.jpg" }
          ]
        }
        """);

        Assert.Equal(
            "https://is1-ssl.mzstatic.com/image/thumb/Music/abc/source/100x100bb.jpg",
            albums[0].ArtworkUrl);
    }

    [Fact]
    public void A_row_with_no_artwork_parses_to_a_blank_url_not_a_crash()
    {
        var albums = AppleMusicCatalog.ParseSearchResults(SampleJson);

        Assert.Equal("", albums[0].ArtworkUrl);
    }

    [Fact]
    public void Asking_for_a_bigger_cover_swaps_only_the_size()
    {
        var big = AppleMusicCatalog.ArtworkAtSize(
            "https://is1-ssl.mzstatic.com/image/thumb/Music/abc/source/100x100bb.jpg", 600);

        Assert.Equal("https://is1-ssl.mzstatic.com/image/thumb/Music/abc/source/600x600bb.jpg", big);
    }

    [Fact]
    public void Asking_for_a_bigger_cover_keeps_whatever_crop_code_follows_the_size()
    {
        var big = AppleMusicCatalog.ArtworkAtSize("https://example.com/art/100x100bb-60.jpg", 300);

        Assert.Equal("https://example.com/art/300x300bb-60.jpg", big);
    }

    [Fact]
    public void There_is_no_bigger_copy_of_a_cover_that_doesnt_exist()
    {
        Assert.Null(AppleMusicCatalog.ArtworkAtSize(null, 600));
        Assert.Null(AppleMusicCatalog.ArtworkAtSize("", 600));
    }

    [Fact]
    public void A_cover_url_in_an_unexpected_shape_is_left_alone_rather_than_mangled()
    {
        Assert.Equal("https://example.com/cover.jpg",
            AppleMusicCatalog.ArtworkAtSize("https://example.com/cover.jpg", 600));
    }

    /// <summary>
    /// The Mac's way past an album that isn't in the library: a link Music itself claims, so the
    /// album page opens in the app rather than a browser.
    /// </summary>
    [Fact]
    public void An_albums_page_opens_in_the_Music_app_not_a_browser()
    {
        var album = AppleMusicCatalog.ParseSearchResults("""
            {"results":[{"collectionId":1440653096,"collectionName":"Dummy","artistName":"Portishead",
              "trackCount":11,"collectionViewUrl":"https://music.apple.com/us/album/dummy/1440653096?uo=4"}]}
            """).Single();

        Assert.Equal("music://music.apple.com/us/album/dummy/1440653096", AppleMusicCatalog.MusicAppUrl(album));
    }

    [Fact]
    public void An_album_with_no_page_link_still_opens_by_its_id()
    {
        var album = new AppleMusicAlbum(1440653096, "Dummy", "Portishead", 11);

        Assert.Equal("music://music.apple.com/us/album/1440653096", AppleMusicCatalog.MusicAppUrl(album));
    }

    // ----- Original over remaster over deluxe -----
    // Apple Music lists an album several ways at once. The playlist wants the record as released:
    // the original, else a remaster, and a deluxe edition only when there's nothing leaner.

    private static AppleMusicAlbum Album(long id, string name, int tracks, string artist = "Portishead",
                                         bool cleaned = false) =>
        new(id, name, artist, tracks, Cleaned: cleaned);

    [Fact]
    public void The_original_beats_a_remaster_and_a_deluxe_edition()
    {
        var albums = new List<AppleMusicAlbum>
        {
            Album(1, "Dummy (Deluxe Edition)", 24),
            Album(2, "Dummy (2014 Remastered)", 11),
            Album(3, "Dummy", 11),
        };

        Assert.Equal(3, AppleMusicCatalog.FindBestMatch(albums, "Portishead", "Dummy")!.CollectionId);
    }

    [Fact]
    public void A_remaster_beats_a_deluxe_edition_even_with_more_tracks_on_neither_side()
    {
        var albums = new List<AppleMusicAlbum>
        {
            Album(1, "Haunted Dancehall - 25th Anniversary", 14),
            Album(2, "Haunted Dancehall (Remastered)", 14),
        };

        Assert.Equal(2, AppleMusicCatalog.FindBestMatch(albums, "Portishead", "Haunted Dancehall")!.CollectionId);
    }

    [Fact]
    public void A_deluxe_edition_is_used_only_when_nothing_leaner_exists()
    {
        var albums = new List<AppleMusicAlbum> { Album(1, "Dummy (Super Deluxe)", 30) };

        Assert.Equal(1, AppleMusicCatalog.FindBestMatch(albums, "Portishead", "Dummy")!.CollectionId);
    }

    /// <summary>
    /// The bug that started this: the fewest-tracks rule handed a one-song listing the win. A single
    /// sharing the album's name is never the album, however few tracks it has.
    /// </summary>
    [Fact]
    public void A_single_named_like_the_album_is_never_picked()
    {
        var albums = new List<AppleMusicAlbum>
        {
            Album(1, "Dummy - Single", 1),
            Album(2, "Dummy - EP", 4),
            Album(3, "Dummy", 11),
        };

        Assert.Equal(3, AppleMusicCatalog.FindBestMatch(albums, "Portishead", "Dummy")!.CollectionId);
    }

    [Fact]
    public void A_sequel_is_not_an_edition_of_the_first_album()
    {
        var albums = new List<AppleMusicAlbum>
        {
            Album(1, "Led Zeppelin II (Remaster)", 9, "Led Zeppelin"),
            Album(2, "Led Zeppelin (Remaster)", 9, "Led Zeppelin"),
        };

        Assert.Equal(2, AppleMusicCatalog.FindBestMatch(albums, "Led Zeppelin", "Led Zeppelin")!.CollectionId);
    }

    [Fact]
    public void The_explicit_album_beats_its_bleeped_twin()
    {
        var albums = new List<AppleMusicAlbum>
        {
            Album(1, "Ill Communication", 20, "Beastie Boys", cleaned: true),
            Album(2, "Ill Communication", 20, "Beastie Boys"),
        };

        Assert.Equal(2, AppleMusicCatalog.FindBestMatch(albums, "Beastie Boys", "Ill Communication")!.CollectionId);
    }

    [Fact]
    public void The_lists_own_furniture_doesnt_stop_a_match()
    {
        var albums = new List<AppleMusicAlbum> { Album(1, "Elvis Presley", 12, "Elvis Presley") };

        Assert.Equal(1, AppleMusicCatalog.FindBestMatch(albums, "Elvis Presley", "Elvis Presley (self titled)")!.CollectionId);
    }

    [Theory]
    [InlineData("Dummy", AlbumEdition.Original)]
    [InlineData("Tago Mago (2011 Remastered)", AlbumEdition.Remaster)]
    [InlineData("Pet Sounds [Mono]", AlbumEdition.Remaster)]
    [InlineData("In Utero - 30th Anniversary", AlbumEdition.Deluxe)]
    [InlineData("Doggystyle (Deluxe Edition)", AlbumEdition.Deluxe)]
    [InlineData("Joan Baez (Bonus Track Version)", AlbumEdition.Deluxe)]
    [InlineData("Abbey Road (Remastered Super Deluxe)", AlbumEdition.Deluxe)]
    [InlineData("Glory Box - Single", AlbumEdition.Single)]
    [InlineData("Unplugged (Live)", AlbumEdition.Other)]
    [InlineData("Live - Throwing Copper", AlbumEdition.Original)]
    public void Editions_are_read_off_the_name(string name, AlbumEdition expected)
    {
        Assert.Equal(expected, AppleMusicCatalog.EditionOf(name));
    }

    /// <summary>
    /// iTunes's /search for "Method Man Tical" has no Tical in it at all — only Tical 2000. A title
    /// that merely starts the same way is a different record, not an edition of this one.
    /// </summary>
    [Fact]
    public void A_sequel_that_starts_with_the_same_word_is_not_the_album()
    {
        var albums = new List<AppleMusicAlbum> { Album(1, "Tical 2000: Judgement Day", 28, "Method Man") };

        Assert.Null(AppleMusicCatalog.FindBestMatch(albums, "Method Man", "Tical"));
    }

    [Fact]
    public void A_subtitle_after_a_colon_can_stand_in_when_nothing_else_matches()
    {
        var albums = new List<AppleMusicAlbum> { Album(1, "Kid A Mnesia: Kid A", 10, "Radiohead"), Album(2, "Kid A", 10, "Radiohead") };
        Assert.Equal(2, AppleMusicCatalog.FindBestMatch(albums, "Radiohead", "Kid A")!.CollectionId);

        var onlySubtitled = new List<AppleMusicAlbum> { Album(3, "Tago Mago: 40th", 7, "Can") };
        Assert.Equal(3, AppleMusicCatalog.FindBestMatch(onlySubtitled, "Can", "Tago Mago")!.CollectionId);
    }
}
