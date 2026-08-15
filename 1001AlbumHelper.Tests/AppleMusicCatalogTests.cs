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
}
