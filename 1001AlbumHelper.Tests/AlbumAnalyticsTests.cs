using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The decade/genre bucketing behind the Analytics window. Getting these wrong doesn't crash
/// anything; it just quietly mislabels or drops albums from the chart.
/// </summary>
public class AlbumAnalyticsTests
{
    private static AlbumEntry Entry(string rating, string year, string genre = "", int row = 1) =>
        new(row, "1", rating, "Title", "Artist", year, genre);

    [Fact]
    public void Buckets_years_into_the_decade_they_start()
    {
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1965"),
            Entry(RatingSession.Starred, "1969"),
            Entry(RatingSession.Liked, "1970"),
        };

        var buckets = AlbumAnalytics.DecadeBreakdown(albums);

        Assert.Equal(2, buckets.Count);
        Assert.Equal("1960s", buckets[0].Label);
        Assert.Equal(2, buckets[0].Count);
        Assert.Equal("1970s", buckets[1].Label);
        Assert.Equal(1, buckets[1].Count);
    }

    [Fact]
    public void Averages_the_score_within_a_decade()
    {
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1971"),  // 4
            Entry(RatingSession.Trash, "1975"),    // 1
        };

        var buckets = AlbumAnalytics.DecadeBreakdown(albums);

        var only = Assert.Single(buckets);
        Assert.Equal(2.5, only.AverageScore);
    }

    [Fact]
    public void Excludes_unrated_and_unparsable_years_from_decades()
    {
        var albums = new[]
        {
            Entry("", "1975"),           // unrated
            Entry(RatingSession.Liked, ""), // blank year
            Entry(RatingSession.Liked, "not a year"),
        };

        Assert.Empty(AlbumAnalytics.DecadeBreakdown(albums));
    }

    [Fact]
    public void Splits_comma_joined_genres_into_separate_buckets()
    {
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1970", "Rock, Jazz"),
            Entry(RatingSession.Liked, "1980", "Rock"),
        };

        var buckets = AlbumAnalytics.GenreBreakdown(albums);

        Assert.Equal(2, buckets.Count);
        Assert.Equal("Rock", buckets[0].Label);
        Assert.Equal(2, buckets[0].Count);
        Assert.Equal("Jazz", buckets[1].Label);
        Assert.Equal(1, buckets[1].Count);
    }

    [Fact]
    public void Excludes_unrated_and_blank_genre_albums_from_genre_breakdown()
    {
        var albums = new[]
        {
            Entry("", "1970", "Rock"),           // unrated
            Entry(RatingSession.Liked, "1980", ""), // no genre
        };

        Assert.Empty(AlbumAnalytics.GenreBreakdown(albums));
    }

    [Fact]
    public void Sorts_genres_by_count_descending()
    {
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1970", "Jazz"),
            Entry(RatingSession.Liked, "1971", "Rock"),
            Entry(RatingSession.Liked, "1972", "Rock"),
        };

        var buckets = AlbumAnalytics.GenreBreakdown(albums);

        Assert.Equal(new[] { "Rock", "Jazz" }, buckets.Select(b => b.Label));
    }

    [Fact]
    public void Counts_ratings_in_legend_order_including_zero_for_ones_never_given()
    {
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1970"),
            Entry(RatingSession.Starred, "1971"),
            Entry(RatingSession.Trash, "1972"),
        };

        var buckets = AlbumAnalytics.RatingDistribution(albums);

        Assert.Equal(4, buckets.Count); // all four ratings appear, even the ones nobody got
        Assert.Equal(new[] { "⭐", "👍", "👎", "❌" }, buckets.Select(b => b.Label));
        Assert.Equal(2, buckets[0].Count);
        Assert.Equal(0, buckets[1].Count);
        Assert.Equal(1, buckets[3].Count);
    }

    [Fact]
    public void Ranks_best_genres_by_average_score_not_count()
    {
        var albums = new[]
        {
            // Rock: four albums, mediocre average.
            Entry(RatingSession.Liked, "1970", "Rock"),
            Entry(RatingSession.Liked, "1971", "Rock"),
            Entry(RatingSession.Disliked, "1972", "Rock"),
            Entry(RatingSession.Disliked, "1973", "Rock"),
            // Jazz: three albums, all starred — higher average, fewer albums.
            Entry(RatingSession.Starred, "1970", "Jazz"),
            Entry(RatingSession.Starred, "1971", "Jazz"),
            Entry(RatingSession.Starred, "1972", "Jazz"),
        };

        var best = AlbumAnalytics.BestGenres(albums, minCount: 3);

        Assert.Equal("Jazz", best[0].Label);
        Assert.Equal("Rock", best[1].Label);
    }

    [Fact]
    public void Excludes_genres_below_the_minimum_count_from_best_genres()
    {
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1970", "Ambient"), // only one — shouldn't win on a fluke
            Entry(RatingSession.Liked, "1971", "Rock"),
            Entry(RatingSession.Liked, "1972", "Rock"),
            Entry(RatingSession.Liked, "1973", "Rock"),
        };

        var best = AlbumAnalytics.BestGenres(albums, minCount: 3);

        Assert.DoesNotContain(best, b => b.Label == "Ambient");
        Assert.Single(best);
    }

    [Fact]
    public void Only_credits_each_albums_primary_genre_not_every_co_tag()
    {
        // Discogs lists every genre that applies to a release, primary first — a film tie-in
        // album comes back e.g. "Rock, Pop, Stage & Screen". If every co-tag counted equally, a
        // handful of unrelated ⭐ albums that all happen to carry the same incidental secondary
        // tag could make it "win" outright, even though none of them are actually about film.
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1964", "Rock, Pop, Stage & Screen"),
            Entry(RatingSession.Starred, "1970", "Pop, Stage & Screen"),
            Entry(RatingSession.Starred, "1971", "Jazz, Funk / Soul, Stage & Screen"),
            // Rock is the real, consistently-liked genre here — just never quite perfect.
            Entry(RatingSession.Liked, "1980", "Rock"),
            Entry(RatingSession.Liked, "1981", "Rock"),
            Entry(RatingSession.Liked, "1982", "Rock"),
        };

        var best = AlbumAnalytics.BestGenres(albums, minCount: 3);

        Assert.DoesNotContain(best, b => b.Label == "Stage & Screen");
        Assert.Contains(best, b => b.Label == "Rock");
    }

    [Fact]
    public void Summarizes_completion_average_and_favorites()
    {
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1970", "Jazz"),
            Entry(RatingSession.Starred, "1971", "Jazz"),
            Entry(RatingSession.Starred, "1972", "Jazz"),
            Entry(RatingSession.Trash, "1980", "Rock"),
            Entry("", "1990"), // unrated — counts toward the total but not "rated"
        };

        var summary = AlbumAnalytics.Summarize(albums);

        Assert.Equal(5, summary.TotalAlbums);
        Assert.Equal(4, summary.RatedAlbums);
        Assert.Equal(3.25, summary.AverageScore); // (4+4+4+1) / 4
        Assert.Equal("Jazz", summary.FavoriteGenre); // needs minCount 3 — Rock has only 1
        Assert.Null(summary.FavoriteDecade); // no decade reaches the minCount-5 bar
    }

    private static CandidateAlbum Candidate(string year, string genre = "", CandidateStatus status = CandidateStatus.Pending) =>
        new() { Title = "Title", Artist = "Artist", Year = year, Genre = genre, Status = status };

    [Fact]
    public void Buckets_shortlist_years_into_decades()
    {
        var candidates = new[]
        {
            Candidate("1975"),
            Candidate("1978"),
            Candidate("1982"),
        };

        var buckets = AlbumAnalytics.CandidateDecadeBreakdown(candidates);

        Assert.Equal(2, buckets.Count);
        Assert.Equal("1970s", buckets[0].Label);
        Assert.Equal(2, buckets[0].Count);
        Assert.Equal("1980s", buckets[1].Label);
        Assert.Equal(1, buckets[1].Count);
    }

    [Fact]
    public void Excludes_non_pending_candidates_from_decade_breakdown()
    {
        var candidates = new[]
        {
            Candidate("1975", status: CandidateStatus.Added),
            Candidate("1980", status: CandidateStatus.Declined),
        };

        Assert.Empty(AlbumAnalytics.CandidateDecadeBreakdown(candidates));
    }

    [Fact]
    public void Splits_and_sorts_shortlist_genres_by_count()
    {
        var candidates = new[]
        {
            Candidate("1970", "Jazz"),
            Candidate("1971", "Rock, Jazz"),
            Candidate("1972", "Rock"),
        };

        var buckets = AlbumAnalytics.CandidateGenreBreakdown(candidates);

        Assert.Equal(new[] { "Jazz", "Rock" }, buckets.Select(b => b.Label));
        Assert.Equal(2, buckets[0].Count);
        Assert.Equal(2, buckets[1].Count);
    }

    [Fact]
    public void Excludes_non_pending_and_blank_genre_candidates_from_genre_breakdown()
    {
        var candidates = new[]
        {
            Candidate("1970", "Jazz", CandidateStatus.Added),
            Candidate("1971", ""),
        };

        Assert.Empty(AlbumAnalytics.CandidateGenreBreakdown(candidates));
    }
}
