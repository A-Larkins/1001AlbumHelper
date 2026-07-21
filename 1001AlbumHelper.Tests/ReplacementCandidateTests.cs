using System.Runtime.CompilerServices;
using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// The candidates file is the only record of which albums have been ruled out, so what matters is
/// that a decision survives the round trip to disk intact.
/// </summary>
public class ReplacementCandidateTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "candidates-tests-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "candidates.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Round_trips_every_field()
    {
        ReplacementCandidates.Save(Path_, new[]
        {
            new CandidateAlbum
            {
                Title = "Vs.", Artist = "Pearl Jam", Year = "1993",
                Genre = "Alternative", Status = CandidateStatus.Pending,
            },
        });

        var album = Assert.Single(ReplacementCandidates.Load(Path_));

        Assert.Equal("Vs.", album.Title);
        Assert.Equal("Pearl Jam", album.Artist);
        Assert.Equal("1993", album.Year);
        Assert.Equal("Alternative", album.Genre);
        Assert.Equal(CandidateStatus.Pending, album.Status);
    }

    [Fact]
    public void Keeps_decided_albums_so_they_are_never_offered_twice()
    {
        ReplacementCandidates.Save(Path_, new[]
        {
            new CandidateAlbum { Title = "Kept", Artist = "A", Status = CandidateStatus.Added },
            new CandidateAlbum { Title = "Dropped", Artist = "B", Status = CandidateStatus.Declined },
            new CandidateAlbum { Title = "Undecided", Artist = "C", Status = CandidateStatus.Pending },
        });

        var loaded = ReplacementCandidates.Load(Path_);

        Assert.Equal(3, loaded.Count);
        Assert.Equal(CandidateStatus.Added, loaded[0].Status);
        Assert.Equal(CandidateStatus.Declined, loaded[1].Status);
        Assert.Single(loaded, a => a.Status == CandidateStatus.Pending);
    }

    [Fact]
    public void A_missing_file_is_an_empty_shortlist_not_a_crash()
    {
        Assert.Empty(ReplacementCandidates.Load(Path.Combine(_dir, "nope.json")));
    }

    [Fact]
    public void Notes_are_per_attempt_feedback_and_stay_out_of_the_file()
    {
        ReplacementCandidates.Save(Path_, new[]
        {
            new CandidateAlbum { Title = "Vs.", Artist = "Pearl Jam", Note = "Discogs says 1993" },
        });

        Assert.DoesNotContain("Discogs", File.ReadAllText(Path_));
        Assert.Equal("", ReplacementCandidates.Load(Path_)[0].Note);
    }

    // ----- Change notification -----
    // The window saves the shortlist off the back of these, so a year typed into a row is only
    // durable for as long as the setter keeps announcing itself.

    [Fact]
    public void Setting_a_year_announces_itself()
    {
        var album = new CandidateAlbum { Title = "Messengers", Artist = "August Burns Red" };
        var heard = new List<string?>();
        album.PropertyChanged += (_, e) => heard.Add(e.PropertyName);

        album.Year = "2007";

        Assert.Contains(nameof(CandidateAlbum.Year), heard);
    }

    [Fact]
    public void Re_setting_the_same_year_stays_quiet()
    {
        // Otherwise every no-op assignment would rewrite the file.
        var album = new CandidateAlbum { Year = "2007" };
        var heard = new List<string?>();
        album.PropertyChanged += (_, e) => heard.Add(e.PropertyName);

        album.Year = "2007";

        Assert.Empty(heard);
    }

    [Fact]
    public void A_note_is_not_mistaken_for_a_year()
    {
        var album = new CandidateAlbum { Title = "Vs.", Artist = "Pearl Jam" };
        var heard = new List<string?>();
        album.PropertyChanged += (_, e) => heard.Add(e.PropertyName);

        album.Note = "Already on the 1001 list.";

        Assert.DoesNotContain(nameof(CandidateAlbum.Year), heard);
    }

    // ----- Adding an album that may already be on the shortlist -----

    private static readonly CandidateAlbum[] Shortlist =
    {
        new() { Title = "Vs.", Artist = "Pearl Jam", Status = CandidateStatus.Pending },
        new() { Title = "Zuma", Artist = "Neil Young", Status = CandidateStatus.Added },
        new() { Title = "Purple", Artist = "Stone Temple Pilots", Status = CandidateStatus.Declined },
    };

    [Fact]
    public void An_album_not_on_the_shortlist_is_new()
    {
        var (outcome, match) = ReplacementCandidates.Classify(Shortlist, "Core", "Stone Temple Pilots");

        Assert.Equal(CandidateAddOutcome.New, outcome);
        Assert.Null(match);
    }

    [Fact]
    public void One_still_waiting_is_not_added_twice()
    {
        var (outcome, _) = ReplacementCandidates.Classify(Shortlist, "Vs.", "Pearl Jam");
        Assert.Equal(CandidateAddOutcome.AlreadyPending, outcome);
    }

    [Fact]
    public void One_already_kept_is_reported_rather_than_re_added()
    {
        var (outcome, _) = ReplacementCandidates.Classify(Shortlist, "Zuma", "Neil Young");
        Assert.Equal(CandidateAddOutcome.AlreadyKept, outcome);
    }

    [Fact]
    public void One_dropped_before_reopens_its_own_row()
    {
        // A second row for the same album would let it be decided on twice.
        var (outcome, match) = ReplacementCandidates.Classify(
            Shortlist, "Purple", "Stone Temple Pilots");

        Assert.Equal(CandidateAddOutcome.Reopen, outcome);
        Assert.Equal("Purple", match!.Title);
    }

    [Theory]
    [InlineData("vs.", "pearl jam")]              // case
    [InlineData("Vs", "Pearl Jam")]               // punctuation
    [InlineData("  Vs.  ", "  Pearl Jam  ")]      // padding
    public void Matching_is_loose_enough_to_catch_a_near_duplicate(string title, string artist)
    {
        var (outcome, _) = ReplacementCandidates.Classify(Shortlist, title, artist);
        Assert.Equal(CandidateAddOutcome.AlreadyPending, outcome);
    }

    // ----- Sorting the shortlist for display -----

    private static readonly CandidateAlbum[] ToSort =
    {
        new() { Title = "Zuma", Artist = "Neil Young", Genre = "Rock", Year = "1975" },
        new() { Title = "The Bends", Artist = "Radiohead", Genre = "Alternative", Year = "1995" },
        new() { Title = "Aja", Artist = "Steely Dan", Genre = "Jazz Rock", Year = "1977" },
        new() { Title = "Debut", Artist = "Björk", Genre = "Electronic", Year = "" },
    };

    [Fact]
    public void Sorts_titles_ignoring_a_leading_the()
    {
        // "The Bends" files under B, so it lands before "Debut" and "Zuma", not up at the top.
        var order = ReplacementCandidates.Sort(ToSort, CandidateSortColumn.Title, descending: false)
            .Select(a => a.Title);

        Assert.Equal(new[] { "Aja", "The Bends", "Debut", "Zuma" }, order);
    }

    [Fact]
    public void Descending_flips_the_order()
    {
        var order = ReplacementCandidates.Sort(ToSort, CandidateSortColumn.Title, descending: true)
            .Select(a => a.Title);

        Assert.Equal(new[] { "Zuma", "Debut", "The Bends", "Aja" }, order);
    }

    [Fact]
    public void Sorts_years_numerically()
    {
        var order = ReplacementCandidates.Sort(ToSort, CandidateSortColumn.Year, descending: false)
            .Where(a => a.Year.Length > 0)
            .Select(a => a.Year);

        Assert.Equal(new[] { "1975", "1977", "1995" }, order);
    }

    [Fact]
    public void A_missing_year_always_sorts_last()
    {
        // Ascending or descending, the album with no year yet is least useful at the top.
        Assert.Equal("Debut",
            ReplacementCandidates.Sort(ToSort, CandidateSortColumn.Year, descending: false)[^1].Title);
        Assert.Equal("Debut",
            ReplacementCandidates.Sort(ToSort, CandidateSortColumn.Year, descending: true)[^1].Title);
    }

    [Fact]
    public void Ties_keep_their_file_order()
    {
        // A stable sort means re-sorting the same column never reshuffles equal rows.
        var sameGenre = new[]
        {
            new CandidateAlbum { Title = "First", Artist = "A", Genre = "Rock" },
            new CandidateAlbum { Title = "Second", Artist = "B", Genre = "Rock" },
            new CandidateAlbum { Title = "Third", Artist = "C", Genre = "Rock" },
        };

        var order = ReplacementCandidates.Sort(sameGenre, CandidateSortColumn.Genre, descending: false)
            .Select(a => a.Title);

        Assert.Equal(new[] { "First", "Second", "Third" }, order);
    }

    /// <summary>
    /// The shipped file, found via the source tree rather than <see cref="Operations.ProjectDir"/>
    /// — under the test host that resolves to the test project, not the app's.
    /// </summary>
    private static string SeedPath([CallerFilePath] string thisFile = "") =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(thisFile)!)!,
            "1001AlbumHelper", ReplacementCandidates.FileName);

    [Fact]
    public void The_shipped_shortlist_is_usable()
    {
        // The app writes to this file as it goes, so its statuses and years are whatever the last
        // session left behind. Only what stays true of it as it's used is worth asserting.
        var seed = ReplacementCandidates.Load(SeedPath());

        Assert.NotEmpty(seed);
        Assert.All(seed, a =>
        {
            Assert.NotEmpty(a.Title);
            Assert.NotEmpty(a.Artist);
        });

        // Two rows for the same album would offer it twice and add it twice.
        var duplicates = seed
            .GroupBy(a => (NumberedList.Normalize(a.Title), NumberedList.Normalize(a.Artist)))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Item1)
            .ToList();
        Assert.Empty(duplicates);
    }
}
