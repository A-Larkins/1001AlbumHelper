using System.Linq;
using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// Playlist 2 is the recommendations queue, so an album found in it that the app has never seen was
/// queued in Apple Music by hand — a potential replacement nobody has told the shortlist about. What
/// matters here is that those arrive as pending candidates, and that an album already ruled on keeps
/// the ruling: the shortlist is the record of what's been decided, and a pull-down mustn't reopen it.
/// </summary>
public class ShortlistIntakeTests
{
    private static PlaylistEntry Album(string title, string artist, string year = "") =>
        new(title, artist, year);

    private static CandidateAlbum Candidate(string title, string artist, CandidateStatus status) =>
        new() { Title = title, Artist = artist, Status = status };

    [Fact]
    public void An_album_the_shortlist_has_never_seen_arrives_as_a_pending_candidate()
    {
        var shortlist = new List<CandidateAlbum>();

        var result = ShortlistIntake.Merge(shortlist, new[] { Album("Lies", "LIES", "2024") });

        var added = Assert.Single(result.Added);
        Assert.Equal("Lies", added.Title);
        Assert.Equal("LIES", added.Artist);
        Assert.Equal("2024", added.Year);
        Assert.Equal(CandidateStatus.Pending, added.Status);
        Assert.Same(added, Assert.Single(shortlist));
    }

    [Fact]
    public void Apple_musics_missing_year_is_left_blank_rather_than_guessed()
    {
        var shortlist = new List<CandidateAlbum>();

        ShortlistIntake.Merge(shortlist, new[] { Album("Tago Mago", "Can") });

        Assert.Equal("", Assert.Single(shortlist).Year);
    }

    [Fact]
    public void The_album_goes_on_the_shortlist_under_its_own_name_not_the_pressings()
    {
        // A kept candidate becomes a line on the replacements list, so the edition furniture Apple
        // Music carries has no business coming with it.
        var shortlist = new List<CandidateAlbum>();

        ShortlistIntake.Merge(shortlist, new[] { Album("Cassini (5th Anniversary Remaster)", "Sithu Aye") });

        Assert.Equal("Cassini", Assert.Single(shortlist).Title);
    }

    [Fact]
    public void The_shortened_title_still_recognises_the_album_on_the_next_pull()
    {
        var shortlist = new List<CandidateAlbum>();
        ShortlistIntake.Merge(shortlist, new[] { Album("Cassini (5th Anniversary Remaster)", "Sithu Aye") });

        var again = ShortlistIntake.Merge(shortlist, new[] { Album("Cassini (5th Anniversary Remaster)", "Sithu Aye") });

        Assert.Empty(again.Added);
        Assert.Single(shortlist);
    }

    [Fact]
    public void An_album_the_working_list_already_had_still_reaches_the_shortlist()
    {
        // The case that bit us: a pull on the phone took fifteen albums into its working list, so
        // neither device called them new again — and the twelve the shortlist had never heard of
        // were stranded. What the shortlist holds is the only thing that decides now.
        var shortlist = new List<CandidateAlbum> { Candidate("Songs for the Deaf", "Queens of the Stone Age", CandidateStatus.Pending) };

        var result = ShortlistIntake.Merge(shortlist, new[]
        {
            Album("Songs for the Deaf", "Queens of the Stone Age"),   // long since on the shortlist
            Album("Raising Sand", "Robert Plant & Alison Krauss"),    // in the playlist, never written down
        });

        Assert.Equal("Raising Sand", Assert.Single(result.Added).Title);
    }

    [Fact]
    public void Running_it_again_over_the_same_playlist_changes_nothing()
    {
        var shortlist = new List<CandidateAlbum>();
        var playlist = new[] { Album("Raising Sand", "Robert Plant & Alison Krauss") };

        ShortlistIntake.Merge(shortlist, playlist);
        var again = ShortlistIntake.Merge(shortlist, playlist);

        Assert.Empty(again.Added);
        Assert.Single(shortlist);
    }

    [Fact]
    public void One_already_waiting_on_the_shortlist_is_not_added_twice()
    {
        var shortlist = new List<CandidateAlbum> { Candidate("The Avalanche", "Owen", CandidateStatus.Pending) };

        var result = ShortlistIntake.Merge(shortlist, new[] { Album("The Avalanche", "Owen") });

        Assert.Empty(result.Added);
        Assert.Empty(result.PreviouslyDropped);
        Assert.Single(shortlist);
    }

    [Fact]
    public void One_already_kept_is_left_alone_it_is_on_the_replacements_list_already()
    {
        var shortlist = new List<CandidateAlbum> { Candidate("Vs.", "Pearl Jam", CandidateStatus.Added) };

        var result = ShortlistIntake.Merge(shortlist, new[] { Album("Vs.", "Pearl Jam") });

        Assert.Empty(result.Added);
        Assert.Empty(result.PreviouslyDropped);
        Assert.Equal(CandidateStatus.Added, Assert.Single(shortlist).Status);
    }

    [Fact]
    public void One_dropped_before_stays_dropped_but_is_named_rather_than_silently_skipped()
    {
        var shortlist = new List<CandidateAlbum> { Candidate("Vs.", "Pearl Jam", CandidateStatus.Declined) };

        var result = ShortlistIntake.Merge(shortlist, new[] { Album("Vs.", "Pearl Jam") });

        Assert.Empty(result.Added);
        Assert.Equal("Vs.", Assert.Single(result.PreviouslyDropped).Title);
        Assert.Equal(CandidateStatus.Declined, Assert.Single(shortlist).Status);
        Assert.Contains("dropped from the shortlist", result.Summary);
    }

    [Theory]
    [InlineData("the avalanche", "owen")]          // case
    [InlineData("Avalanche", "The Owen")]          // a leading "the", either side
    [InlineData("The Avalanche!", "Owen")]         // punctuation
    public void Matching_is_loose_enough_that_the_catalogs_spelling_cant_duplicate_a_row(
        string title, string artist)
    {
        var shortlist = new List<CandidateAlbum> { Candidate("The Avalanche", "Owen", CandidateStatus.Pending) };

        var result = ShortlistIntake.Merge(shortlist, new[] { Album(title, artist) });

        Assert.Empty(result.Added);
        Assert.Single(shortlist);
    }

    [Fact]
    public void Two_editions_of_the_same_album_in_one_playlist_only_make_one_row()
    {
        var shortlist = new List<CandidateAlbum>();

        var result = ShortlistIntake.Merge(shortlist, new[]
        {
            Album("Tago Mago", "Can"),
            Album("Tago Mago (2011 Remastered)", "Can"),
        });

        Assert.Single(result.Added);
        Assert.Single(shortlist);
    }

    [Fact]
    public void Nothing_arriving_changes_nothing()
    {
        var shortlist = new List<CandidateAlbum> { Candidate("The Avalanche", "Owen", CandidateStatus.Pending) };

        var result = ShortlistIntake.Merge(shortlist, Array.Empty<PlaylistEntry>());

        Assert.Empty(result.Added);
        Assert.Equal("", result.Summary);
        Assert.Single(shortlist);
    }

    [Fact]
    public void A_big_haul_is_counted_in_full_but_only_the_first_few_are_named()
    {
        var albums = Enumerable.Range(1, 7).Select(i => Album($"Album {i}", $"Artist {i}")).ToArray();

        var result = ShortlistIntake.Merge(new List<CandidateAlbum>(), albums);

        Assert.Equal(7, result.Added.Count);
        Assert.Contains("7 added to the shortlist", result.Summary);
        Assert.Contains("Album 4 — Artist 4", result.Summary);
        Assert.Contains("and 3 more", result.Summary);
        Assert.DoesNotContain("Album 5", result.Summary);
    }

    [Fact]
    public void A_yearless_arrival_is_counted_as_still_needing_one()
    {
        // The lookup itself needs Discogs, so what's checked here is the bookkeeping around it:
        // an album with no year is reported as wanting one rather than passing quietly.
        var result = ShortlistIntake.Merge(new List<CandidateAlbum>(), new[] { Album("Homebound", "Sithu Aye") });

        Assert.Equal("Homebound", Assert.Single(result.StillWithoutAYear).Title);
        Assert.Contains("still has no year", result.Summary);
    }

    [Fact]
    public void An_album_that_landed_yearless_earlier_gets_another_go_at_one()
    {
        // It's already on the shortlist, so nothing is added — but it's still undecided and still
        // yearless, so the next pull hands it to the lookup rather than passing over it.
        var shortlist = new List<CandidateAlbum> { Candidate("Homebound", "Sithu Aye", CandidateStatus.Pending) };

        var result = ShortlistIntake.Merge(shortlist, new[] { Album("Homebound", "Sithu Aye") });

        Assert.Empty(result.Added);
        Assert.Same(shortlist[0], Assert.Single(result.Waiting!));
        Assert.Single(result.StillWithoutAYear);
    }

    [Fact]
    public void An_album_that_already_has_a_year_is_not_looked_up_again()
    {
        var shortlist = new List<CandidateAlbum> { Candidate("Raising Sand", "Robert Plant", CandidateStatus.Pending) };
        shortlist[0].Year = "2007";

        var result = ShortlistIntake.Merge(shortlist, new[] { Album("Raising Sand", "Robert Plant") });

        Assert.Empty(result.StillWithoutAYear);
        Assert.DoesNotContain("no year", result.Summary);
    }

    [Fact]
    public void A_decided_album_is_left_out_of_the_year_hunt()
    {
        var shortlist = new List<CandidateAlbum> { Candidate("Vs.", "Pearl Jam", CandidateStatus.Added) };

        var result = ShortlistIntake.Merge(shortlist, new[] { Album("Vs.", "Pearl Jam") });

        Assert.Empty(result.Waiting!);
    }

    [Fact]
    public void The_summary_names_what_went_on_the_shortlist()
    {
        var result = ShortlistIntake.Merge(new List<CandidateAlbum>(), new[]
        {
            Album("Lies", "LIES"),
            Album("Tago Mago", "Can"),
        });

        Assert.Contains("2 added to the shortlist", result.Summary);
        Assert.Contains("Lies — LIES", result.Summary);
        Assert.Contains("Tago Mago — Can", result.Summary);
    }
}
