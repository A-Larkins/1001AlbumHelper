using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The machinery behind "Keep" on a Playlist 2 row — the button that reaches the replacements list
/// itself, not just the shortlist.
/// <para>
/// These go at <see cref="Operations.AddReplacementAlbumAsync(ISheetsClient, string, string, string, string, string, int)"/>
/// and <see cref="Operations.ReachedTheList"/> rather than at <c>KeepFromPlaylistAsync</c>, which
/// orchestrates them. That is deliberate and load-bearing: <c>KeepFromPlaylistAsync</c> builds a
/// <see cref="CandidateRepository"/> of its own, and a repository built inside a test reaches the
/// <em>real</em> Potentials sheet — the service-account key is an embedded resource of the shared
/// assembly, which the test project references (see PROJECT.md §7). Everything below runs against
/// <see cref="FakeSheets"/> and touches nothing outside it.
/// </para>
/// </summary>
public class PlaylistKeepTests
{
    private const string Replacements = "*my replacement albums";
    private const string Albums = "1001 albums";
    private const string MustHear = "Must Hear";

    private static FakeSheets Sheet(string[][] replacements, params string[][] theThousandAndOne)
    {
        var repRows = new List<string[]> { new[] { "#", "Album", "Artist", "Year" } };
        repRows.AddRange(replacements);

        var masterRows = new List<string[]> { new[] { "#", "Rating", "Album", "Artist", "Year", "Genre" } };
        masterRows.AddRange(theThousandAndOne);

        return new FakeSheets(Replacements, repRows)
            .Add(Albums, masterRows)
            .Add(MustHear, new List<string[]> { new[] { "#", "Album", "Artist", "Year" } });
    }

    private static string[] Replacement(int n, string title, string artist, string year) =>
        new[] { n.ToString(), title, artist, year };

    private static string[] OnThe1001(int n, string title, string artist, string year) =>
        new[] { n.ToString(), "", title, artist, year, "" };

    /// <summary>What Keep does once it has a title and a year — the only part that writes.</summary>
    private static Task<Operations.AddResult> AddAsync(FakeSheets sheets, string title, string artist, int year) =>
        Operations.AddReplacementAlbumAsync(sheets, Replacements, Albums, MustHear, title, artist, year);

    /// <summary>The album's rows on the replacements tab, header excluded.</summary>
    private static List<List<string>> Entries(FakeSheets sheets) => sheets.Rows(Replacements).Skip(1).ToList();

    [Fact]
    public async Task Keeping_an_album_puts_it_on_the_replacements_list()
    {
        var sheets = Sheet(new[] { Replacement(1, "Homebound", "Sithu Aye", "2015") });

        var result = await AddAsync(sheets, "Ukulele Songs", "Eddie Vedder", 2011);

        Assert.Equal(Operations.AddOutcome.Added, result.Outcome);
        Assert.Contains(Entries(sheets), r => r.Contains("Ukulele Songs"));
    }

    /// <summary>
    /// Apple Music stocks the pressing, not the album, and the replacements list is a document
    /// people read — so Keep trims the edition furniture off first, exactly as an intake does.
    /// </summary>
    [Theory]
    [InlineData("Cassini (5th Anniversary Remaster)", "Cassini")]
    [InlineData("Tago Mago (2011 Remastered)", "Tago Mago")]
    [InlineData("Piano - EP", "Piano - EP")]
    public void The_album_name_goes_on_the_list_not_the_pressings(string stocked, string expected)
    {
        Assert.Equal(expected, DiscogsApiClient.BareTitle(stocked));
    }

    /// <summary>
    /// The replacements list exists for albums the 1001 doesn't cover, so this is a refusal — and
    /// <see cref="Operations.ReachedTheList"/> has to agree, because that is what stops the
    /// shortlist recording a keep that never happened.
    /// </summary>
    [Fact]
    public async Task An_album_already_on_the_1001_is_refused_and_nothing_is_written()
    {
        var sheets = Sheet(Array.Empty<string[]>(), OnThe1001(763, "Illmatic", "Nas", "1994"));

        var result = await AddAsync(sheets, "Illmatic", "Nas", 1994);

        Assert.Equal(Operations.AddOutcome.AlreadyIn1001, result.Outcome);
        Assert.False(Operations.ReachedTheList(result.Outcome));
        Assert.Empty(Entries(sheets));
    }

    /// <summary>
    /// Already there is not a failure: the album is where it was wanted, so the decision stands and
    /// gets recorded — but no second row is written for it.
    /// </summary>
    [Fact]
    public async Task An_album_already_on_the_replacements_list_is_left_alone()
    {
        var sheets = Sheet(new[] { Replacement(1, "Homebound", "Sithu Aye", "2015") });

        var result = await AddAsync(sheets, "Homebound", "Sithu Aye", 2015);

        Assert.Equal(Operations.AddOutcome.AlreadyInReplacements, result.Outcome);
        Assert.True(Operations.ReachedTheList(result.Outcome));
        Assert.Single(Entries(sheets));
    }

    /// <summary>The list is ordered by year, and Keep has to leave it that way.</summary>
    [Fact]
    public async Task A_kept_album_lands_in_its_year_block()
    {
        var sheets = Sheet(new[]
        {
            Replacement(1, "Early", "A", "1970"),
            Replacement(2, "Late", "C", "1990"),
        });

        await AddAsync(sheets, "Middle", "B", 1980);

        Assert.Equal(new[] { "Early", "Middle", "Late" },
                     Entries(sheets).Select(r => r[1]));
    }

    /// <summary>The two outcomes that mean the album really is on the list, and the three that don't.</summary>
    [Theory]
    [InlineData(Operations.AddOutcome.Added, true)]
    [InlineData(Operations.AddOutcome.AlreadyInReplacements, true)]
    [InlineData(Operations.AddOutcome.AlreadyIn1001, false)]
    [InlineData(Operations.AddOutcome.NotConfigured, false)]
    [InlineData(Operations.AddOutcome.Failed, false)]
    public void Only_a_real_add_counts_as_a_decision(Operations.AddOutcome outcome, bool landed)
    {
        Assert.Equal(landed, Operations.ReachedTheList(outcome));
    }
}
