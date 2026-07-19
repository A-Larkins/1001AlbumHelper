using System.Text.Json;
using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The rules that decide which album — and which <em>year</em> — the dropdown offers. Getting
/// these wrong doesn't crash anything; it just quietly writes the wrong year into the list.
/// </summary>
public class AlbumRankingTests
{
    /// <summary>Builds one Discogs-shaped search result.</summary>
    private static JsonElement Result(
        string title, string? year = "1970", int have = 0, params string[] formats)
    {
        var payload = new
        {
            title,
            year,
            format = formats.Length > 0 ? formats : new[] { "Vinyl", "LP", "Album" },
            community = new { have, want = 0 },
        };
        return JsonSerializer.SerializeToElement(payload);
    }

    [Fact]
    public void Collapses_repeated_masters_and_keeps_the_widely_owned_pressing()
    {
        // Discogs carries three "Blue Train" masters. The 1958 one is the release everyone owns;
        // 1957 is a recording-date entry and 2010 a reissue. Taking the earliest year would
        // answer 1957 — plausible-looking and wrong.
        var results = new[]
        {
            Result("John Coltrane - Blue Train", "1957", have: 812),
            Result("John Coltrane - Blue Train", "1958", have: 115688),
            Result("John Coltrane - Blue Train", "2010", have: 4210),
        };

        var ranked = DiscogsApiClient.RankAlbumResults(results);

        var only = Assert.Single(ranked);
        Assert.Equal("Blue Train", only.Title);
        Assert.Equal("1958", only.Year);
    }

    [Fact]
    public void Treats_case_and_spacing_differences_as_the_same_album()
    {
        var results = new[]
        {
            Result("Radiohead - OK Computer", "1997", have: 262676),
            Result("radiohead  -  ok   computer", "2016", have: 40),
        };

        var ranked = DiscogsApiClient.RankAlbumResults(results);

        Assert.Single(ranked);
        Assert.Equal("1997", ranked[0].Year);
    }

    [Fact]
    public void Orders_by_how_many_people_own_it()
    {
        var results = new[]
        {
            Result("The Cynics - Blue Train Station", have: 1657),
            Result("John Coltrane - Blue Train", have: 115688),
            Result("Johnny Cash - All Aboard The Blue Train", have: 5469),
        };

        var ranked = DiscogsApiClient.RankAlbumResults(results);

        Assert.Equal(
            new[] { "Blue Train", "All Aboard The Blue Train", "Blue Train Station" },
            ranked.Select(r => r.Title));
    }

    [Fact]
    public void Drops_bootlegs_and_box_sets()
    {
        // A bootleg carries the real album's name and year, so it would otherwise look like a
        // legitimate answer; a box set is a packaging of albums rather than an album.
        var results = new[]
        {
            Result("Radiohead - The Bends", "2005", have: 900, "CD", "Album", "Unofficial Release"),
            Result("Radiohead - Album Box Set", "2007", have: 5000, "CD", "Album", "Box Set"),
            Result("Radiohead - The Bends", "1995", have: 135015),
        };

        var ranked = DiscogsApiClient.RankAlbumResults(results);

        var only = Assert.Single(ranked);
        Assert.Equal("The Bends", only.Title);
        Assert.Equal("1995", only.Year);
    }

    [Fact]
    public void Skips_results_it_cannot_read_an_artist_from()
    {
        var results = new[]
        {
            Result("NoSeparatorHere", have: 999),
            Result("Nirvana - Nevermind", "1991", have: 404793),
        };

        var ranked = DiscogsApiClient.RankAlbumResults(results);

        Assert.Equal("Nirvana", Assert.Single(ranked).Artist);
    }

    [Fact]
    public void Survives_a_result_with_no_year_or_community_block()
    {
        var bare = JsonSerializer.SerializeToElement(new { title = "The Section - Strung Out" });

        var ranked = DiscogsApiClient.RankAlbumResults(new[] { bare });

        var only = Assert.Single(ranked);
        Assert.Equal("", only.Year);
        Assert.Equal(0, only.Have);
        // With no year there's nothing to prefill, so the label must not imply one.
        Assert.Equal("Strung Out — The Section", only.Display);
    }

    [Fact]
    public void Labels_a_suggestion_with_its_year()
    {
        var suggestion = new AlbumSuggestion("Blue Train", "John Coltrane", "1958", 115688);

        Assert.Equal("Blue Train — John Coltrane (1958)", suggestion.Display);
    }

    [Fact]
    public void Caps_the_shortlist()
    {
        var many = Enumerable.Range(0, 40)
            .Select(i => Result($"Artist {i} - Album {i}", have: i))
            .ToArray();

        Assert.Equal(12, DiscogsApiClient.RankAlbumResults(many).Count);
        Assert.Equal(5, DiscogsApiClient.RankAlbumResults(many, take: 5).Count);
    }
}
