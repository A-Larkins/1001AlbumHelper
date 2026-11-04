namespace _1001AlbumHelper;

/// <summary>
/// Turns the master list's ratings into the numbers and breakdowns the Insights page and its PDF
/// show. Pure and testable — no sheet or network access here.
/// <para>
/// <b>What counts.</b> Every album you've <em>heard</em>: the four scores ⭐ 👍 👎 ❌ plus ✓. A ✓
/// is an album listened to and never given a score — most of the 1960s–80s were marked that way,
/// with only the ⭐s scored afterward. So a ✓ is not an unknown: it's a known "not a ⭐" (a ⭐ is
/// what puts an album on Must Hear, and a ✓ never got one). Leaving ✓ out — as this page first
/// did — reads the 1970s as 129 ⭐ out of 130, when it's 129 out of 278.
/// </para>
/// <para>
/// <b>How favorites are picked.</b> Because ✓ albums carry no score, an average rating can't be
/// taken fairly across a decade or genre: the scored ones are mostly the ⭐s. What <em>is</em>
/// known for every heard album is whether it got a ⭐, so rankings use the ⭐ rate — the share of
/// heard albums that got one — among genres (or decades) with at least <see cref="MinAlbums"/>
/// heard, so three-for-three can't top the list. Ties go to the one with more albums, then
/// alphabetically, never to dictionary order (which is how five Glam records once "beat" forty
/// Hard Rock ones). A weighted (Bayesian) rate was tried and dropped: it ranks a little better
/// in theory, but the list it gives isn't in the order of the percentages printed beside it.
/// </para>
/// </summary>
public static class AlbumAnalytics
{
    /// <summary>A genre or decade needs this many heard albums to be ranked at all.</summary>
    public const int MinAlbums = 10;

    /// <summary>
    /// Discogs' top-level genres. A genre string from Discogs lists the release's specific
    /// <em>styles</em> first and these broad genres after ("Hard Rock, Blues Rock, Rock") — and since
    /// nearly everything here carries "Rock", the styles are what tell albums apart.
    /// <para>"Folk, World, &amp; Country" is one genre with commas in its name, so it has to be
    /// recognized before the string is split (see <see cref="SplitTags"/>).</para>
    /// </summary>
    public static readonly IReadOnlySet<string> BroadGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Blues", "Brass & Military", "Children's", "Classical", "Electronic", FolkWorldCountry,
        "Funk / Soul", "Hip Hop", "Jazz", "Latin", "Non-Music", "Pop", "Reggae", "Rock", "Stage & Screen",
    };

    private const string FolkWorldCountry = "Folk, World, & Country";

    /// <summary>The five marks a heard album can carry, in the legend's order: ⭐ 👍 👎 ❌ then ✓.</summary>
    public static readonly string[] Marks =
    {
        RatingSession.Starred, RatingSession.Liked, RatingSession.Disliked, RatingSession.Trash, RatingSession.Listened,
    };

    /// <summary>How many heard albums carry each mark.</summary>
    public sealed record RatingMix(int Starred, int Liked, int Disliked, int Trash, int Listened)
    {
        public static readonly RatingMix None = new(0, 0, 0, 0, 0);

        /// <summary>Every album heard, ✓ included.</summary>
        public int Heard => Scored + Listened;

        /// <summary>Those carrying one of the four scores.</summary>
        public int Scored => Starred + Liked + Disliked + Trash;

        /// <summary>The share of heard albums that got a ⭐, 0–1. A ✓ counts as heard and not starred.</summary>
        public double StarShare => Heard == 0 ? 0 : (double)Starred / Heard;

        /// <summary>
        /// ⭐ = 4 … ❌ = 1 across the <em>scored</em> albums only (0 with none). Biased upward wherever
        /// ✓s are common, since those are the unscored non-⭐s — so it's shown as a detail, never ranked on.
        /// </summary>
        public double AverageScore => Scored == 0 ? 0 : (4.0 * Starred + 3 * Liked + 2 * Disliked + Trash) / Scored;

        public int CountOf(string mark) => mark.Trim() switch
        {
            RatingSession.Starred => Starred,
            RatingSession.Liked => Liked,
            RatingSession.Disliked => Disliked,
            RatingSession.Trash => Trash,
            RatingSession.Listened => Listened,
            _ => 0,
        };

        /// <summary>This mix with one more album marked <paramref name="mark"/>; unchanged for anything else.</summary>
        public RatingMix With(string mark) => mark.Trim() switch
        {
            RatingSession.Starred => this with { Starred = Starred + 1 },
            RatingSession.Liked => this with { Liked = Liked + 1 },
            RatingSession.Disliked => this with { Disliked = Disliked + 1 },
            RatingSession.Trash => this with { Trash = Trash + 1 },
            RatingSession.Listened => this with { Listened = Listened + 1 },
            _ => this,
        };

        public static RatingMix Of(IEnumerable<AlbumEntry> albums) =>
            albums.Aggregate(None, (mix, a) => mix.With(a.Rating));
    }

    /// <summary>
    /// One genre's or decade's albums: <see cref="Count"/> heard (or, on the shortlist, pending) and
    /// their <see cref="Mix"/>. Shortlist buckets have no ratings, so their mix is empty.
    /// </summary>
    public sealed record Bucket(string Label, int Count)
    {
        public RatingMix Mix { get; init; } = RatingMix.None;
    }

    /// <summary>
    /// A share (0–1) as a percentage, for every figure the page and the PDFs print. Whole numbers
    /// by default, with <paramref name="decimals"/> places on request — but never a rounding that
    /// lies: a share that isn't zero never reads "0%" (1 of 775 is "0.1%"), and one short of
    /// everything never reads "100%" ("99.6%"). Past one decimal place it's "&lt;0.1%".
    /// </summary>
    public static string Percent(double share, int decimals = 0)
    {
        double pct = share * 100;
        bool misleading(int d) => (share > 0 && Math.Round(pct, d) == 0) || (share < 1 && Math.Round(pct, d) >= 100);
        if (decimals == 0 && misleading(0)) decimals = 1;
        if (share > 0 && Math.Round(pct, decimals) == 0)
            return $"<{Math.Pow(10, -decimals).ToString(System.Globalization.CultureInfo.CurrentCulture)}%";
        if (share < 1 && Math.Round(pct, decimals) >= 100)
            return $">{(100 - Math.Pow(10, -decimals)).ToString(System.Globalization.CultureInfo.CurrentCulture)}%";
        return pct.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), System.Globalization.CultureInfo.CurrentCulture) + "%";
    }

    /// <summary>Whether the album has been listened to: one of the four scores, or ✓.</summary>
    public static bool IsHeard(AlbumEntry album) =>
        RatingMix.None.With(album.Rating) != RatingMix.None;

    /// <summary>Only the albums listened to.</summary>
    public static List<AlbumEntry> Heard(IEnumerable<AlbumEntry> albums) => albums.Where(IsHeard).ToList();

    /// <summary>A genre string's tags, in order, with "Folk, World, &amp; Country" kept whole.</summary>
    public static List<string> SplitTags(string genre)
    {
        const string placeholder = "\u0001";
        return (genre ?? "")
            .Replace(FolkWorldCountry, placeholder, StringComparison.OrdinalIgnoreCase)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t == placeholder ? FolkWorldCountry : t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The genres an album is counted under: its specific styles ("Hard Rock", "Boom Bap"), not the
    /// broad Discogs genres every album shares. An album tagged with broad genres only (Apple Music's
    /// one-word genres often are) is counted under those instead, so it isn't left out entirely.
    /// </summary>
    public static List<string> GenresOf(string genre)
    {
        var tags = SplitTags(genre);
        var styles = tags.Where(t => !BroadGenres.Contains(t)).ToList();
        return styles.Count > 0 ? styles : tags;
    }

    /// <summary>Heard albums per decade (1950s, 1960s, …), oldest first.</summary>
    public static List<Bucket> DecadeBreakdown(IEnumerable<AlbumEntry> albums)
    {
        return Heard(albums)
            .Select(a => (Album: a, Decade: DecadeOf(a.Year)))
            .Where(x => x.Decade is not null)
            .GroupBy(x => x.Decade!.Value)
            .OrderBy(g => g.Key)
            .Select(g => MakeBucket($"{g.Key}s", g.Select(x => x.Album)))
            .ToList();
    }

    /// <summary>Heard albums per genre (see <see cref="GenresOf"/>), most first.</summary>
    public static List<Bucket> GenreBreakdown(IEnumerable<AlbumEntry> albums)
    {
        return Heard(albums)
            .SelectMany(a => GenresOf(a.Genre).Select(g => (Album: a, Genre: g)))
            .GroupBy(x => x.Genre, StringComparer.OrdinalIgnoreCase)
            .Select(g => MakeBucket(g.Key, g.Select(x => x.Album)))
            .OrderByDescending(b => b.Count)
            .ThenByDescending(b => b.Mix.StarShare)
            .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Genres with at least <paramref name="minCount"/> heard albums, highest ⭐ rate first; ties
    /// go to more albums heard, then alphabetically.
    /// </summary>
    public static List<Bucket> FavoriteGenres(IEnumerable<AlbumEntry> albums, int minCount = MinAlbums) =>
        GenreBreakdown(albums).Where(b => b.Count >= minCount)
            .OrderByDescending(b => b.Mix.StarShare).ThenByDescending(b => b.Count)
            .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The same genres, lowest ⭐ rate first; ties go to more albums heard (more evidence), then
    /// alphabetically.
    /// </summary>
    public static List<Bucket> LeastFavoriteGenres(IEnumerable<AlbumEntry> albums, int minCount = MinAlbums) =>
        GenreBreakdown(albums).Where(b => b.Count >= minCount)
            .OrderBy(b => b.Mix.StarShare).ThenByDescending(b => b.Count)
            .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Decades with at least <paramref name="minCount"/> heard albums, highest ⭐ rate first.</summary>
    public static List<Bucket> FavoriteDecades(IEnumerable<AlbumEntry> albums, int minCount = MinAlbums) =>
        DecadeBreakdown(albums).Where(b => b.Count >= minCount)
            .OrderByDescending(b => b.Mix.StarShare).ThenByDescending(b => b.Count)
            .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The page's headline numbers.</summary>
    /// <param name="TotalAlbums">Everything on the master list.</param>
    /// <param name="Mix">Every heard album's mark.</param>
    public sealed record TasteSummary(
        int TotalAlbums, RatingMix Mix, Bucket? FavoriteDecade, Bucket? FavoriteGenre, Bucket? LeastFavoriteGenre)
    {
        public int HeardAlbums => Mix.Heard;
    }

    /// <summary>
    /// Builds the headline numbers from the full master list (heard and not, so progress can be
    /// worked out). The least favorite is only named when at least two genres qualify — with one,
    /// it would be the favorite as well.
    /// </summary>
    public static TasteSummary Summarize(IReadOnlyCollection<AlbumEntry> allAlbums)
    {
        var heard = Heard(allAlbums);
        var favorites = FavoriteGenres(heard);
        return new TasteSummary(
            allAlbums.Count, RatingMix.Of(heard),
            FavoriteDecades(heard).FirstOrDefault(),
            favorites.FirstOrDefault(),
            favorites.Count >= 2 ? LeastFavoriteGenres(heard).First() : null);
    }

    // ---------- The albums you added ----------

    /// <summary>One decade's share of your additions beside its share of the 1001 as a whole.</summary>
    public sealed record DecadeShare(string Label, int Added, double AddedShare, int InThe1001, double The1001Share);

    /// <summary>An artist on your additions, and whether the 1001 has them too.</summary>
    public sealed record ArtistCount(string Artist, int Albums, bool OnThe1001);

    /// <summary>
    /// The albums added to the finished list in place of the ones cut — the Replacements list. It
    /// has no ratings or genres (it's four columns: number, title, artist, year), so what can be
    /// said is when the records are from and who made them, measured against the 1001.
    /// </summary>
    /// <param name="NewArtists">Artists on your additions with no album at all on the 1001.</param>
    /// <param name="ShareSince2000">Of your additions, the share released 2000 or later; likewise for the 1001.</param>
    public sealed record AdditionsSummary(
        int Count, int Artists, int NewArtists,
        double ShareSince2000, double The1001ShareSince2000,
        IReadOnlyList<DecadeShare> Decades, IReadOnlyList<ArtistCount> TopArtists)
    {
        public ArtistCount? TopArtist => TopArtists.FirstOrDefault();
    }

    public static AdditionsSummary SummarizeAdditions(
        IReadOnlyCollection<NumberedList.Row> additions, IReadOnlyCollection<AlbumEntry> the1001, int topArtists = 10)
    {
        var the1001Artists = the1001.Select(a => ArtistKey(a.Artist)).Where(k => k.Length > 0).ToHashSet();

        var artists = additions
            .Where(r => ArtistKey(r.Artist).Length > 0)
            .GroupBy(r => ArtistKey(r.Artist))
            .Select(g => new ArtistCount(g.First().Artist.Trim(), g.Count(), the1001Artists.Contains(g.Key)))
            .ToList();

        var addedByDecade = additions.Select(r => DecadeOf(r.Year)).Where(d => d is not null)
            .GroupBy(d => d!.Value).ToDictionary(g => g.Key, g => g.Count());
        var listByDecade = the1001.Select(a => DecadeOf(a.Year)).Where(d => d is not null)
            .GroupBy(d => d!.Value).ToDictionary(g => g.Key, g => g.Count());
        int addedDated = addedByDecade.Values.Sum(), listDated = listByDecade.Values.Sum();

        var decades = addedByDecade.Keys.Union(listByDecade.Keys).Order()
            .Select(d =>
            {
                int added = addedByDecade.GetValueOrDefault(d), inList = listByDecade.GetValueOrDefault(d);
                return new DecadeShare($"{d}s", added, Share(added, addedDated), inList, Share(inList, listDated));
            })
            .ToList();

        return new AdditionsSummary(
            additions.Count, artists.Count, artists.Count(a => !a.OnThe1001),
            Share(addedByDecade.Where(kv => kv.Key >= 2000).Sum(kv => kv.Value), addedDated),
            Share(listByDecade.Where(kv => kv.Key >= 2000).Sum(kv => kv.Value), listDated),
            decades,
            artists.Where(a => a.Albums >= 2)
                .OrderByDescending(a => a.Albums).ThenBy(a => a.Artist, StringComparer.OrdinalIgnoreCase)
                .Take(topArtists).ToList());
    }

    /// <summary>
    /// An artist name for matching across lists: <see cref="NumberedList.Normalize"/>, less the
    /// "(2)" Discogs adds to tell same-named acts apart ("The Crickets (2)").
    /// </summary>
    private static string ArtistKey(string artist) =>
        NumberedList.Normalize(System.Text.RegularExpressions.Regex.Replace(artist ?? "", @"\s*\(\d+\)\s*$", ""));

    private static double Share(int part, int whole) => whole == 0 ? 0 : (double)part / whole;

    // ---------- Everything the page and the PDFs show ----------

    /// <summary>
    /// The whole Insights page as data, built once and drawn by the page, its PDF, and the
    /// finished-list PDF's appendix alike — so the three can't disagree.
    /// </summary>
    public sealed record Report(
        TasteSummary Taste,
        IReadOnlyList<Bucket> Decades,
        IReadOnlyList<Bucket> MostHeardGenres,
        IReadOnlyList<Bucket> FavoriteGenres,
        IReadOnlyList<Bucket> LeastFavoriteGenres,
        int AlbumsWithoutGenre,
        int MustHearCount,
        AdditionsSummary Additions,
        int ShortlistPending,
        int ShortlistKept,
        IReadOnlyList<Bucket> ShortlistDecades,
        IReadOnlyList<Bucket> ShortlistGenres);

    /// <param name="master">The 1001, rated.</param>
    /// <param name="mustHearCount">Albums kept from the 1001 (the Must Hear list).</param>
    /// <param name="additions">The Replacements list.</param>
    /// <param name="candidates">The shortlist, every status.</param>
    public static Report BuildReport(
        IReadOnlyCollection<AlbumEntry> master, int mustHearCount,
        IReadOnlyCollection<NumberedList.Row> additions, IReadOnlyCollection<CandidateAlbum> candidates)
    {
        var heard = Heard(master);
        var favorites = FavoriteGenres(heard).Take(8).ToList();
        // Never the same genre in both lists — with only a few qualifying, they'd otherwise overlap.
        var least = LeastFavoriteGenres(heard).Where(b => !favorites.Contains(b)).Take(5).ToList();

        return new Report(
            Summarize(master),
            DecadeBreakdown(heard),
            GenreBreakdown(heard).Take(10).ToList(),
            favorites,
            least,
            heard.Count(a => a.Genre.Trim().Length == 0),
            mustHearCount,
            SummarizeAdditions(additions, master),
            candidates.Count(c => c.Status == CandidateStatus.Pending),
            candidates.Count(c => c.Status == CandidateStatus.Added),
            CandidateDecadeBreakdown(candidates),
            CandidateGenreBreakdown(candidates).Take(10).ToList());
    }

    /// <summary>Count per decade across the still-pending shortlist, oldest first. No ratings yet, so no scores.</summary>
    public static List<Bucket> CandidateDecadeBreakdown(IEnumerable<CandidateAlbum> candidates) => candidates
        .Where(c => c.Status == CandidateStatus.Pending)
        .Select(c => DecadeOf(c.Year))
        .Where(d => d is not null)
        .GroupBy(d => d!.Value)
        .OrderBy(g => g.Key)
        .Select(g => new Bucket($"{g.Key}s", g.Count()))
        .ToList();

    /// <summary>
    /// Count per genre (see <see cref="GenresOf"/>) across the still-pending shortlist, most first.
    /// Many shortlist genres came from Apple Music ("Alternative", "R&amp;B/Soul"), so they're put in
    /// the words the 1001's Discogs genres use (<see cref="AlbumGenres.FromAppleMusic"/>).
    /// </summary>
    public static List<Bucket> CandidateGenreBreakdown(IEnumerable<CandidateAlbum> candidates) => candidates
        .Where(c => c.Status == CandidateStatus.Pending && c.Genre.Trim().Length > 0)
        .SelectMany(c => GenresOf(c.Genre))
        .Select(g => AlbumGenres.FromAppleMusic(g) ?? g)
        .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
        .Select(g => new Bucket(g.Key, g.Count()))
        .OrderByDescending(b => b.Count)
        .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static Bucket MakeBucket(string label, IEnumerable<AlbumEntry> albums)
    {
        var mix = RatingMix.Of(albums);
        return new Bucket(label, mix.Heard) { Mix = mix };
    }

    private static int? DecadeOf(string year) =>
        int.TryParse(year.Trim(), out int y) && y > 1000 ? y - y % 10 : null;
}
