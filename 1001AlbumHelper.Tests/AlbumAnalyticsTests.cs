using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The decade/genre bucketing and favorite-picking behind the Insights page. Getting these wrong doesn't crash
/// anything; it just quietly mislabels or drops albums from the chart.
/// </summary>
public class AlbumAnalyticsTests
{
    private static AlbumEntry Entry(string rating, string year, string genre = "", int row = 1) =>
        new(row, "1", rating, "Title", "Artist", year, genre);

    private static List<AlbumEntry> Many(int n, string rating, string year, string genre = "") =>
        Enumerable.Range(0, n).Select(_ => Entry(rating, year, genre)).ToList();

    [Fact]
    public void Buckets_years_into_the_decade_they_start()
    {
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1965"),
            Entry(RatingSession.Listened, "1969"),
            Entry(RatingSession.Liked, "1970"),
        };

        var buckets = AlbumAnalytics.DecadeBreakdown(albums);

        Assert.Equal(new[] { "1960s", "1970s" }, buckets.Select(b => b.Label));
        Assert.Equal(new[] { 2, 1 }, buckets.Select(b => b.Count));
    }

    [Fact]
    public void Counts_a_listened_album_as_heard_but_not_starred()
    {
        // The bug: ✓ albums were left out entirely, and most of the 1970s was marked ✓ with only
        // the ⭐s ever scored — so the decade read as 129 ⭐ out of 130, "99%", when it was 129 out
        // of 278.
        var albums = Many(129, RatingSession.Starred, "1974")
            .Concat(Many(1, RatingSession.Liked, "1975"))
            .Concat(Many(148, RatingSession.Listened, "1976"))
            .ToList();

        var seventies = Assert.Single(AlbumAnalytics.DecadeBreakdown(albums));

        Assert.Equal(278, seventies.Count);
        Assert.Equal(148, seventies.Mix.Listened);
        Assert.Equal(129.0 / 278, seventies.Mix.StarShare, 6);
    }

    [Fact]
    public void Leaves_unheard_and_unparsable_years_out_of_decades()
    {
        var albums = new[]
        {
            Entry("", "1975"),               // not heard yet
            Entry(RatingSession.Liked, ""),  // blank year
            Entry(RatingSession.Liked, "not a year"),
        };

        Assert.Empty(AlbumAnalytics.DecadeBreakdown(albums));
    }

    [Fact]
    public void Averages_only_the_scored_albums()
    {
        var mix = AlbumAnalytics.RatingMix.Of(new[]
        {
            Entry(RatingSession.Starred, "1971"),
            Entry(RatingSession.Trash, "1975"),
            Entry(RatingSession.Listened, "1976"), // heard, no score — not a 0
        });

        Assert.Equal(3, mix.Heard);
        Assert.Equal(2, mix.Scored);
        Assert.Equal(2.5, mix.AverageScore);
        Assert.Equal(1.0 / 3, mix.StarShare, 6);
    }

    [Fact]
    public void Keeps_folk_world_and_country_whole_when_splitting_tags()
    {
        // Discogs' one genre with commas in its name — split naively it became "Folk", "World"
        // and a phantom "& Country".
        Assert.Equal(
            new[] { "Country", "Folk, World, & Country" },
            AlbumAnalytics.SplitTags("Country, Folk, World, & Country"));
    }

    [Fact]
    public void Counts_an_album_under_its_styles_not_the_broad_genres_everything_shares()
    {
        Assert.Equal(new[] { "Hard Rock", "Blues Rock" }, AlbumAnalytics.GenresOf("Hard Rock, Blues Rock, Rock"));
        Assert.Equal(new[] { "Ballad", "Vocal" }, AlbumAnalytics.GenresOf("Ballad, Vocal, Jazz, Pop"));
    }

    [Fact]
    public void Falls_back_to_the_broad_genre_when_an_album_has_no_style()
    {
        // Apple Music's genres are often just the broad one.
        Assert.Equal(new[] { "Hip Hop" }, AlbumAnalytics.GenresOf("Hip Hop"));
        Assert.Equal(new[] { "Rock", "Electronic" }, AlbumAnalytics.GenresOf("Rock, Electronic"));
    }

    [Fact]
    public void Leaves_unheard_and_blank_genre_albums_out_of_genres()
    {
        var albums = new[]
        {
            Entry("", "1970", "Hard Rock"),        // not heard yet
            Entry(RatingSession.Liked, "1980", ""), // no genre
        };

        Assert.Empty(AlbumAnalytics.GenreBreakdown(albums));
    }

    [Fact]
    public void Sorts_genres_by_albums_heard_and_carries_the_mix()
    {
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1970", "Bebop, Jazz"),
            Entry(RatingSession.Listened, "1971", "Hard Rock, Rock"),
            Entry(RatingSession.Trash, "1972", "Hard Rock, Rock"),
        };

        var buckets = AlbumAnalytics.GenreBreakdown(albums);

        Assert.Equal(new[] { "Hard Rock", "Bebop" }, buckets.Select(b => b.Label));
        Assert.Equal(new AlbumAnalytics.RatingMix(0, 0, 0, 1, 1), buckets[0].Mix);
    }

    [Fact]
    public void Ranks_favorites_by_star_rate_among_genres_with_enough_albums()
    {
        // The other bug: every small all-⭐ genre tied at the top, and the favorite was whichever
        // the dictionary listed first — five Glam records beat forty Hard Rock ones. Five is too
        // few to be ranked at all now, and the order follows the percentages shown beside it.
        var albums = Many(5, RatingSession.Starred, "1973", "Glam")
            .Concat(Many(9, RatingSession.Starred, "1975", "Thrash")).Concat(Many(2, RatingSession.Listened, "1975", "Thrash"))
            .Concat(Many(43, RatingSession.Starred, "1970", "Blues Rock")).Concat(Many(18, RatingSession.Listened, "1970", "Blues Rock"))
            .Concat(Many(30, RatingSession.Listened, "1980", "Synth-pop"))
            .ToList();

        var ranked = AlbumAnalytics.FavoriteGenres(albums);

        Assert.Equal(new[] { "Thrash", "Blues Rock", "Synth-pop" }, ranked.Select(b => b.Label));
        Assert.True(ranked.Zip(ranked.Skip(1)).All(p => p.First.Mix.StarShare >= p.Second.Mix.StarShare));
    }

    [Fact]
    public void Ranks_on_how_often_a_genre_gets_a_star_not_on_the_average_of_the_scored_few()
    {
        // Post-Punk's only scored albums are its three ⭐s — an "average" of 4.00 — but 24 more
        // were heard and never starred. The ⭐ rate is known for every heard album, so a ✓-heavy
        // genre is never flattered by its handful of scored ⭐s.
        var albums = Many(43, RatingSession.Starred, "1970", "Blues Rock")
            .Concat(Many(17, RatingSession.Listened, "1970", "Blues Rock"))
            .Concat(Many(1, RatingSession.Liked, "1970", "Blues Rock"))
            .Concat(Many(3, RatingSession.Starred, "1970", "Post-Punk"))
            .Concat(Many(24, RatingSession.Listened, "1980", "Post-Punk"))
            .ToList();

        var ranked = AlbumAnalytics.FavoriteGenres(albums);

        Assert.Equal(new[] { "Blues Rock", "Post-Punk" }, ranked.Select(b => b.Label));
        Assert.Equal(4.0, ranked[1].Mix.AverageScore); // its scored albums are all ⭐ — and it still ranks last
    }

    [Fact]
    public void Ranking_ties_go_to_more_albums_then_alphabetical_never_dictionary_order()
    {
        var albums = Many(10, RatingSession.Liked, "1970", "Zydeco")
            .Concat(Many(10, RatingSession.Liked, "1970", "Ambient"))
            .Concat(Many(12, RatingSession.Liked, "1970", "Krautrock"))
            .ToList();

        Assert.Equal(new[] { "Krautrock", "Ambient", "Zydeco" }, AlbumAnalytics.FavoriteGenres(albums).Select(b => b.Label));
    }

    [Fact]
    public void Least_favorites_run_lowest_rate_first_and_ties_go_to_more_evidence()
    {
        var albums = Many(1, RatingSession.Starred, "1980", "Synth-pop").Concat(Many(37, RatingSession.Listened, "1980", "Synth-pop"))
            .Concat(Many(10, RatingSession.Disliked, "1990", "Techno"))
            .Concat(Many(20, RatingSession.Listened, "1990", "Indie Rock"))
            .ToList();

        Assert.Equal(new[] { "Indie Rock", "Techno", "Synth-pop" },
            AlbumAnalytics.LeastFavoriteGenres(albums).Select(b => b.Label));
    }

    [Fact]
    public void Excludes_genres_below_the_minimum_count_from_the_ranking()
    {
        var albums = new[] { Entry(RatingSession.Starred, "1970", "Ambient") } // one — mustn't win on a fluke
            .Concat(Many(3, RatingSession.Liked, "1971", "Hard Rock"))
            .ToList();

        var ranked = AlbumAnalytics.FavoriteGenres(albums, minCount: 3);

        Assert.Equal("Hard Rock", Assert.Single(ranked).Label);
    }

    [Fact]
    public void An_incidental_broad_genre_can_not_win_over_the_styles()
    {
        // A film tie-in comes back "Rock, Pop, Stage & Screen" plus its styles — the broad tags
        // aren't what the album is, so they mustn't be ranked beside the styles.
        var albums = new[]
        {
            Entry(RatingSession.Starred, "1964", "Beat, Rock, Pop, Stage & Screen"),
            Entry(RatingSession.Starred, "1970", "Soundtrack, Pop, Stage & Screen"),
            Entry(RatingSession.Starred, "1971", "Soul-Jazz, Jazz, Funk / Soul, Stage & Screen"),
        }.Concat(Many(3, RatingSession.Liked, "1980", "Hard Rock, Rock")).ToList();

        var ranked = AlbumAnalytics.FavoriteGenres(albums, minCount: 3);

        Assert.DoesNotContain(ranked, b => b.Label == "Stage & Screen");
        Assert.Contains(ranked, b => b.Label == "Hard Rock");
    }

    [Fact]
    public void Summarizes_progress_star_rate_and_favorites()
    {
        var albums = Many(10, RatingSession.Starred, "1970", "Bebop")
            .Concat(Many(6, RatingSession.Disliked, "1980", "Synth-pop"))
            .Concat(Many(4, RatingSession.Listened, "1985", "Synth-pop"))
            .Concat(Many(5, "", "1990"))
            .ToList();

        var summary = AlbumAnalytics.Summarize(albums);

        Assert.Equal(25, summary.TotalAlbums);
        Assert.Equal(20, summary.HeardAlbums);
        Assert.Equal(16, summary.Mix.Scored);
        Assert.Equal(0.5, summary.Mix.StarShare);
        Assert.Equal("Bebop", summary.FavoriteGenre?.Label);
        Assert.Equal("Synth-pop", summary.LeastFavoriteGenre?.Label);
        Assert.Equal("1970s", summary.FavoriteDecade?.Label);
    }

    [Fact]
    public void Names_no_least_favorite_when_only_one_genre_qualifies()
    {
        var summary = AlbumAnalytics.Summarize(Many(10, RatingSession.Starred, "1970", "Bebop"));

        Assert.Equal("Bebop", summary.FavoriteGenre?.Label);
        Assert.Null(summary.LeastFavoriteGenre);
    }

    [Fact]
    public void Best_decade_counts_the_listened_albums_too()
    {
        // The '60s scored only their ⭐s, so on scored albums alone they'd be "100%". Counting
        // their ✓s, the '70s — more ⭐ per album heard — win.
        // A decade of six albums, five of them ⭐, is too few to be anyone's best.
        var albums = Many(39, RatingSession.Starred, "1965")
            .Concat(Many(110, RatingSession.Listened, "1966"))
            .Concat(Many(129, RatingSession.Starred, "1974"))
            .Concat(Many(149, RatingSession.Listened, "1975"))
            .Concat(Many(5, RatingSession.Starred, "2003"))
            .Concat(Many(1, RatingSession.Listened, "2004"))
            .ToList();

        Assert.Equal("1970s", AlbumAnalytics.Summarize(albums).FavoriteDecade?.Label);
    }

    private static NumberedList.Row Added(string artist, string year, string title = "Title") =>
        new(1, "1", title, artist, year);

    [Fact]
    public void Measures_your_additions_against_the_1001()
    {
        var the1001 = new[]
        {
            new AlbumEntry(1, "1", "", "Foxtrot", "Genesis", "1972", ""),
            new AlbumEntry(2, "2", "", "That'll Be The Day", "The Crickets (2)", "1957", ""),
            new AlbumEntry(3, "3", "", "Elephant", "The White Stripes", "2003", ""),
            new AlbumEntry(4, "4", "", "Ten", "Pearl Jam", "1991", ""),
        };
        var additions = new[]
        {
            Added("Genesis", "1973", "Selling England"), Added("Genesis", "1974", "The Lamb"),
            Added("Crickets", "1958"),                       // the "(2)" is Discogs' — same act
            Added("Opeth", "2001"), Added("Opeth", "2005"), Added("Opeth", "2008"),
            Added("Totorro", "2025"),
        };

        var a = AlbumAnalytics.SummarizeAdditions(additions, the1001);

        Assert.Equal(7, a.Count);
        Assert.Equal(4, a.Artists);
        Assert.Equal(2, a.NewArtists); // Opeth, Totorro
        Assert.Equal(4.0 / 7, a.ShareSince2000, 6);
        Assert.Equal(0.25, a.The1001ShareSince2000);
        Assert.Equal(new[] { "Opeth", "Genesis" }, a.TopArtists.Select(t => t.Artist)); // twice or more only
        Assert.False(a.TopArtists[0].OnThe1001);
        Assert.True(a.TopArtists[1].OnThe1001);

        var seventies = a.Decades.Single(d => d.Label == "1970s");
        Assert.Equal(2, seventies.Added);
        Assert.Equal(1, seventies.InThe1001);
        Assert.Contains(a.Decades, d => d.Label == "1990s" && d.Added == 0); // decades either list has
    }

    [Fact]
    public void Builds_one_report_with_favorites_and_least_favorites_never_overlapping()
    {
        // Only two genres qualify: each must appear in one list, not both.
        var master = Many(10, RatingSession.Starred, "1970", "Bebop")
            .Concat(Many(10, RatingSession.Listened, "1980", "Synth-pop"))
            .ToList();

        var report = AlbumAnalytics.BuildReport(master, 10, new[] { Added("Opeth", "2005") }, new[] { Candidate("1999", "Metal") });

        Assert.Equal(new[] { "Bebop", "Synth-pop" }, report.FavoriteGenres.Select(b => b.Label));
        Assert.Empty(report.LeastFavoriteGenres);
        Assert.Equal(1, report.Additions.Count);
        Assert.Equal(1, report.ShortlistPending);
        Assert.Equal("Heavy Metal", Assert.Single(report.ShortlistGenres).Label); // Apple Music's name, put in Discogs' words
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
            Candidate("1970", "Bebop, Jazz"),
            Candidate("1971", "Hard Rock, Bebop, Rock, Jazz"),
            Candidate("1972", "Hard Rock, Rock"),
        };

        var buckets = AlbumAnalytics.CandidateGenreBreakdown(candidates);

        Assert.Equal(new[] { "Bebop", "Hard Rock" }, buckets.Select(b => b.Label));
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
