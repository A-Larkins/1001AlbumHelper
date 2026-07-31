using System.Text.Json;
using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// Genre extraction from a Discogs <c>database/search</c> result. Wrong here means the Analytics
/// window's genre chart quietly mislabels or drops an album — not a crash, so worth pinning down.
/// </summary>
public class DiscogsGenreTests
{
    private static JsonElement Result(string title, string[]? genre = null, string[]? style = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["year"] = "1970",
            ["format"] = new[] { "Vinyl", "LP", "Album" },
            ["community"] = new { have = 1, want = 0 },
        };
        if (genre is not null) payload["genre"] = genre;
        if (style is not null) payload["style"] = style;
        return JsonSerializer.SerializeToElement(payload);
    }

    [Fact]
    public void Joins_multiple_genres_with_a_comma_and_space()
    {
        var result = Result("Radiohead - Kid A", genre: new[] { "Rock", "Electronic" });

        var only = Assert.Single(DiscogsApiClient.RankAlbumResults(new[] { result }));

        Assert.Equal("Rock, Electronic", only.Genre);
    }

    [Fact]
    public void Uses_genre_when_style_is_absent()
    {
        var result = Result("Miles Davis - Kind of Blue", genre: new[] { "Jazz" });

        var only = Assert.Single(DiscogsApiClient.RankAlbumResults(new[] { result }));

        Assert.Equal("Jazz", only.Genre);
    }

    [Fact]
    public void Uses_style_when_genre_is_absent()
    {
        var result = Result("Aphex Twin - Selected Ambient Works 85-92", style: new[] { "Ambient", "IDM" });

        var only = Assert.Single(DiscogsApiClient.RankAlbumResults(new[] { result }));

        Assert.Equal("Ambient, IDM", only.Genre);
    }

    [Fact]
    public void Prefers_the_specific_style_over_the_broad_genre_when_both_are_present()
    {
        // "Electronic" is one of Discogs' ~15 broad buckets — nearly meaningless on its own.
        // "Trip Hop" is what actually says something about how the album sounds, so it should lead.
        var result = Result("Portishead - Dummy", genre: new[] { "Electronic" }, style: new[] { "Trip Hop" });

        var only = Assert.Single(DiscogsApiClient.RankAlbumResults(new[] { result }));

        Assert.Equal("Trip Hop, Electronic", only.Genre);
    }

    [Fact]
    public void Drops_a_genre_thats_already_said_by_style_instead_of_repeating_it()
    {
        var result = Result("Radiohead - Kid A", genre: new[] { "Rock", "Electronic" }, style: new[] { "Electronic" });

        var only = Assert.Single(DiscogsApiClient.RankAlbumResults(new[] { result }));

        Assert.Equal("Electronic, Rock", only.Genre);
    }

    [Fact]
    public void Defaults_to_blank_when_neither_is_present()
    {
        var result = Result("The Section - Strung Out");

        var only = Assert.Single(DiscogsApiClient.RankAlbumResults(new[] { result }));

        Assert.Equal("", only.Genre);
    }
}
