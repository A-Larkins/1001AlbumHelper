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

    /// <summary>
    /// The shipped file, found via the source tree rather than <see cref="Operations.ProjectDir"/>
    /// — under the test host that resolves to the test project, not the app's.
    /// </summary>
    private static string SeedPath([CallerFilePath] string thisFile = "") =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(thisFile)!)!,
            "1001AlbumHelper", ReplacementCandidates.FileName);

    [Fact]
    public void The_shipped_shortlist_parses_and_starts_out_undecided()
    {
        var seed = ReplacementCandidates.Load(SeedPath());

        Assert.NotEmpty(seed);
        Assert.All(seed, a =>
        {
            Assert.NotEmpty(a.Title);
            Assert.NotEmpty(a.Artist);
            Assert.Equal(CandidateStatus.Pending, a.Status);
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
