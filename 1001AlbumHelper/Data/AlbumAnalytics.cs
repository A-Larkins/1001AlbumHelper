namespace _1001AlbumHelper;

/// <summary>
/// Turns rated albums into the decade/genre breakdowns the Analytics window charts. Pure and
/// testable — no sheet or network access here.
/// </summary>
public static class AlbumAnalytics
{
    /// <summary>One bucket's count and average rating score, sorted by count when built by the methods below.</summary>
    public sealed record Bucket(string Label, int Count, double AverageScore);

    /// <summary>
    /// ⭐ is the best rating and ❌ the worst, so a higher score means a better outcome. Unrated
    /// albums have no score and are excluded from every average below.
    /// </summary>
    public static double? ScoreFor(string rating) => rating switch
    {
        RatingSession.Starred => 4,
        RatingSession.Liked => 3,
        RatingSession.Disliked => 2,
        RatingSession.Trash => 1,
        _ => null,
    };

    /// <summary>Count + average score per decade (1970s, 1980s, …), sorted by decade ascending.</summary>
    public static List<Bucket> DecadeBreakdown(IEnumerable<AlbumEntry> albums)
    {
        var byDecade = new SortedDictionary<int, List<double>>();

        foreach (var album in albums)
        {
            var score = ScoreFor(album.Rating);
            if (score is null) continue;
            if (!int.TryParse(album.Year.Trim(), out int year)) continue;

            int decade = year - (year % 10);
            if (!byDecade.TryGetValue(decade, out var scores))
                byDecade[decade] = scores = new List<double>();
            scores.Add(score.Value);
        }

        return byDecade
            .Select(kv => new Bucket($"{kv.Key}s", kv.Value.Count, kv.Value.Average()))
            .ToList();
    }

    /// <summary>Count + average score per genre, sorted by count descending. Comma-joined genre strings are split.</summary>
    public static List<Bucket> GenreBreakdown(IEnumerable<AlbumEntry> albums)
    {
        var byGenre = new Dictionary<string, List<double>>();

        foreach (var album in albums)
        {
            var score = ScoreFor(album.Rating);
            if (score is null || album.Genre.Length == 0) continue;

            foreach (var genre in album.Genre.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!byGenre.TryGetValue(genre, out var scores))
                    byGenre[genre] = scores = new List<double>();
                scores.Add(score.Value);
            }
        }

        return byGenre
            .Select(kv => new Bucket(kv.Key, kv.Value.Count, kv.Value.Average()))
            .OrderByDescending(b => b.Count)
            .ToList();
    }

    /// <summary>How many albums landed on each rating, in the legend's order (⭐ → ❌).</summary>
    public static List<Bucket> RatingDistribution(IEnumerable<AlbumEntry> albums)
    {
        var counts = albums
            .Select(a => a.Rating)
            .Where(r => ScoreFor(r) is not null)
            .GroupBy(r => r)
            .ToDictionary(g => g.Key, g => g.Count());

        return RatingSession.Choices
            .Select(c => new Bucket(c.Symbol, counts.GetValueOrDefault(c.Symbol), ScoreFor(c.Symbol)!.Value))
            .ToList();
    }

    /// <summary>
    /// Genres sorted by how much you've actually liked them (average score) rather than how many
    /// you've heard. <paramref name="minCount"/> keeps a single ⭐ one-off from topping the list —
    /// only genres with enough albums to mean something are ranked.
    /// <para>
    /// Unlike <see cref="GenreBreakdown"/>, this only counts each album's <em>first</em>-listed
    /// (primary) genre. Discogs tags a release with every genre that applies — a Beatles film
    /// tie-in like "A Hard Day's Night" comes back "Rock, Pop, Stage &amp; Screen" — and crediting
    /// every co-tag equally lets an incidental one (here, "Stage &amp; Screen") win outright if a
    /// handful of albums that happen to share it are all ⭐, even though none of them are actually
    /// about film or theatre. The first tag is Discogs' primary classification, so it's the one
    /// that should represent "your favorite genre."
    /// </para>
    /// </summary>
    public static List<Bucket> BestGenres(IEnumerable<AlbumEntry> albums, int minCount = 3)
    {
        var byGenre = new Dictionary<string, List<double>>();

        foreach (var album in albums)
        {
            var score = ScoreFor(album.Rating);
            var primary = PrimaryGenre(album.Genre);
            if (score is null || primary is null) continue;

            if (!byGenre.TryGetValue(primary, out var scores))
                byGenre[primary] = scores = new List<double>();
            scores.Add(score.Value);
        }

        return byGenre
            .Select(kv => new Bucket(kv.Key, kv.Value.Count, kv.Value.Average()))
            .Where(b => b.Count >= minCount)
            .OrderByDescending(b => b.AverageScore)
            .ToList();
    }

    /// <summary>Discogs lists a release's most relevant genre first; "" (blank) has none.</summary>
    private static string? PrimaryGenre(string genre) =>
        genre.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

    /// <summary>The handful of top-line numbers the Analytics window's stat tiles show.</summary>
    public sealed record TasteSummary(
        int TotalAlbums, int RatedAlbums, double AverageScore, string? FavoriteDecade, string? FavoriteGenre);

    /// <summary>
    /// Builds the stat-tile summary from the full master list (rated and unrated alike, so
    /// completion can be worked out) plus a genre/decade minimum count so a favorite isn't just a
    /// single lucky ⭐.
    /// </summary>
    public static TasteSummary Summarize(IReadOnlyCollection<AlbumEntry> allAlbums)
    {
        var rated = allAlbums.Where(a => ScoreFor(a.Rating) is not null).ToList();
        double avg = rated.Count > 0 ? rated.Average(a => ScoreFor(a.Rating)!.Value) : 0;

        var bestDecade = DecadeBreakdown(rated).Where(b => b.Count >= 5).MaxBy(b => b.AverageScore);
        var bestGenre = BestGenres(rated).FirstOrDefault();

        return new TasteSummary(
            allAlbums.Count, rated.Count, avg, bestDecade?.Label, bestGenre?.Label);
    }

    /// <summary>
    /// Count per decade across the still-pending shortlist (potential replacements). There's no
    /// rating to average — these haven't been listened to yet — so every bucket's score is 0.
    /// </summary>
    public static List<Bucket> CandidateDecadeBreakdown(IEnumerable<CandidateAlbum> candidates)
    {
        var byDecade = new SortedDictionary<int, int>();

        foreach (var c in candidates)
        {
            if (c.Status != CandidateStatus.Pending) continue;
            if (!int.TryParse(c.Year.Trim(), out int year)) continue;

            int decade = year - (year % 10);
            byDecade[decade] = byDecade.GetValueOrDefault(decade) + 1;
        }

        return byDecade.Select(kv => new Bucket($"{kv.Key}s", kv.Value, 0)).ToList();
    }

    /// <summary>Count per genre across the still-pending shortlist, sorted by count descending.</summary>
    public static List<Bucket> CandidateGenreBreakdown(IEnumerable<CandidateAlbum> candidates)
    {
        var byGenre = new Dictionary<string, int>();

        foreach (var c in candidates)
        {
            if (c.Status != CandidateStatus.Pending || c.Genre.Length == 0) continue;

            foreach (var genre in c.Genre.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                byGenre[genre] = byGenre.GetValueOrDefault(genre) + 1;
        }

        return byGenre
            .Select(kv => new Bucket(kv.Key, kv.Value, 0))
            .OrderByDescending(b => b.Count)
            .ToList();
    }
}
