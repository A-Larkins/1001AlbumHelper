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
        { "collectionType": "Album", "collectionId": 1440783617, "artistName": "Nirvana", "collectionName": "Nevermind (Remastered)" },
        { "collectionType": "Album", "collectionId": 1631384034, "artistName": "Nirvana", "collectionName": "In Utero - 30th Anniversary" },
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
    public void No_candidates_yields_no_match()
    {
        Assert.Null(AppleMusicCatalog.FindBestMatch(new(), "Nirvana", "Nevermind"));
    }

    // ----- The "nothing lines up" fallback -----
    // A combined artist+title /search query already filtered by both, so its top hit is still a
    // reasonable guess even without an exact title match. An artist's whole /lookup catalog was never
    // filtered by title at all, so the same fallback there would silently hand back a random album.

    [Fact]
    public void An_unmatched_title_falls_back_to_the_first_result_by_default()
    {
        var albums = AppleMusicCatalog.ParseSearchResults(SampleJson);

        var match = AppleMusicCatalog.FindBestMatch(albums, "Nirvana", "Bleach");

        Assert.NotNull(match);
        Assert.Equal(1440783617, match!.CollectionId); // Nevermind — the top hit, not a real match
    }

    [Fact]
    public void Requiring_a_title_match_refuses_to_guess_from_an_unfiltered_catalog()
    {
        var albums = AppleMusicCatalog.ParseSearchResults(SampleJson);

        var match = AppleMusicCatalog.FindBestMatch(albums, "Nirvana", "Bleach", requireTitleMatch: true);

        Assert.Null(match);
    }
}
