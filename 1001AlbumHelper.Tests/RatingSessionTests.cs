using System.Text.RegularExpressions;
using _1001AlbumHelper;

namespace _1001AlbumHelper.Tests;

/// <summary>
/// A spreadsheet in memory, standing in for the live one. Only the slice of A1 notation the app
/// actually uses is understood — single cells, whole columns and rectangular blocks — which is
/// enough for a rating run to be exercised end to end without touching Google.
/// </summary>
internal sealed class FakeSheets : ISheetsClient
{
    private readonly Dictionary<string, List<List<string>>> _tabs = new();

    public FakeSheets(string tab, IEnumerable<string[]> rows) => Add(tab, rows);

    public FakeSheets Add(string tab, IEnumerable<string[]> rows)
    {
        _tabs[tab] = rows.Select(r => r.ToList()).ToList();
        return this;
    }

    /// <summary>One cell as it now stands, by A1 reference — how assertions read the sheet back.</summary>
    public string Cell(string tab, string cellA1)
    {
        var (col, row, _, _) = Parse(cellA1);
        var rows = _tabs[tab];
        if (row - 1 >= rows.Count || col >= rows[row - 1].Count) return "";
        return rows[row - 1][col];
    }

    /// <summary>Every non-blank row of a tab, for asserting on a whole list at once.</summary>
    public List<List<string>> Rows(string tab) =>
        _tabs[tab].Where(r => r.Any(c => c.Length > 0)).ToList();

    public Task<IReadOnlyList<IReadOnlyList<string>>> ReadTabAsync(string tabName, string a1Range)
    {
        var (fromCol, fromRow, toCol, toRow) = Parse(a1Range);
        var rows = _tabs[tabName];

        int last = toRow == 0 ? rows.Count : Math.Min(toRow, rows.Count);
        var result = new List<IReadOnlyList<string>>();
        for (int i = fromRow - 1; i < last; i++)
        {
            var row = rows[i];
            var slice = new List<string>();
            for (int c = fromCol; c <= toCol && c < row.Count; c++) slice.Add(row[c]);
            result.Add(slice);
        }
        return Task.FromResult<IReadOnlyList<IReadOnlyList<string>>>(result);
    }

    public Task UpdateCellAsync(string tabName, string cellA1, string value)
    {
        var (col, row, _, _) = Parse(cellA1);
        Set(tabName, row - 1, col, value);
        return Task.CompletedTask;
    }

    public Task InsertRowAsync(string tabName, int rowNumber, IList<object> values, int? formatSourceRow = null)
    {
        _tabs[tabName].Insert(rowNumber - 1, values.Select(v => v?.ToString() ?? "").ToList());
        return Task.CompletedTask;
    }

    public Task WriteRangeAsync(string tabName, string topLeft, IList<IList<object>> rows)
    {
        var (col, row, _, _) = Parse(topLeft);
        for (int i = 0; i < rows.Count; i++)
            for (int c = 0; c < rows[i].Count; c++)
                Set(tabName, row - 1 + i, col + c, rows[i][c]?.ToString() ?? "");
        return Task.CompletedTask;
    }

    public Task ClearRangeAsync(string tabName, string a1Range)
    {
        var (fromCol, fromRow, toCol, toRow) = Parse(a1Range);
        for (int i = fromRow - 1; i < toRow && i < _tabs[tabName].Count; i++)
            for (int c = fromCol; c <= toCol; c++) Set(tabName, i, c, "");
        return Task.CompletedTask;
    }

    public Task WriteColumnAsync(string tabName, string column, int startRow, IReadOnlyList<string> values)
    {
        int col = ColumnIndex(column);
        for (int i = 0; i < values.Count; i++) Set(tabName, startRow - 1 + i, col, values[i]);
        return Task.CompletedTask;
    }

    private void Set(string tab, int row, int col, string value)
    {
        var rows = _tabs[tab];
        while (rows.Count <= row) rows.Add(new List<string>());
        while (rows[row].Count <= col) rows[row].Add("");
        rows[row][col] = value;
    }

    private static (int FromCol, int FromRow, int ToCol, int ToRow) Parse(string a1)
    {
        var m = Regex.Match(a1, @"^([A-Z]+)(\d*)(?::([A-Z]+)(\d*))?$");
        if (!m.Success) throw new ArgumentException($"Unsupported range: {a1}");

        int fromCol = ColumnIndex(m.Groups[1].Value);
        int fromRow = m.Groups[2].Value.Length > 0 ? int.Parse(m.Groups[2].Value) : 1;
        int toCol = m.Groups[3].Success ? ColumnIndex(m.Groups[3].Value) : fromCol;
        int toRow = m.Groups[4].Value.Length > 0 ? int.Parse(m.Groups[4].Value) : 0; // 0 = to the end
        return (fromCol, fromRow, toCol, toRow);
    }

    private static int ColumnIndex(string letters) =>
        letters.Aggregate(0, (acc, c) => acc * 26 + (c - 'A' + 1)) - 1;
}

/// <summary>
/// Ratings are the one thing in the app that overwrite something already on the sheet, so what's
/// under test is the queue reaching an album that's already rated, and the Must Hear list keeping
/// up when a ⭐ is given or taken away.
/// </summary>
public class RatingSessionTests : IDisposable
{
    // RatingChanges is process-wide, so each test starts from a clean slate and leaves one.
    public RatingSessionTests() => RatingChanges.Reset();
    public void Dispose() => RatingChanges.Reset();

    private const string Albums = "1001 albums";
    private const string MustHear = "Must Hear";

    /// <summary>Rows 1–2 are the legend and header, so album #1 lands on sheet row 3.</summary>
    private static FakeSheets Sheet(params string[][] albums)
    {
        var rows = new List<string[]>
        {
            new[] { "Legend", "", "", "", "", "" },
            new[] { "#", "Rating", "Album", "Artist", "Year", "Genre" },
        };
        rows.AddRange(albums);
        return new FakeSheets(Albums, rows)
            .Add(MustHear, new List<string[]> { new[] { "#", "Album", "Artist", "Year" } });
    }

    private static string[] Album(int number, string rating, string title, string artist, string year) =>
        new[] { number.ToString(), rating, title, artist, year, "" };

    private static Task<RatingSession> OpenAsync(FakeSheets sheets) =>
        RatingSession.LoadAsync(sheets, Albums, MustHear);

    [Fact]
    public async Task Revisit_offers_the_albums_the_other_queues_skip()
    {
        var sheets = Sheet(
            Album(1, "", "Unrated", "A", "1970"),
            Album(2, "✓", "Listened", "B", "1971"),
            Album(3, "👍", "Liked", "C", "1972"),
            Album(4, "⭐", "Starred", "D", "1973"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false);

        Assert.Equal(2, session.Remaining);
        Assert.Equal("Liked", session.Current!.Title);
    }

    [Fact]
    public async Task Revisit_can_be_narrowed_to_one_rating()
    {
        var sheets = Sheet(
            Album(1, "👍", "Liked", "A", "1970"),
            Album(2, "⭐", "Starred", "B", "1971"),
            Album(3, "👍", "Also liked", "C", "1972"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false, ratingFilter: "👍");

        Assert.Equal(2, session.Remaining);
        Assert.Equal("Liked", session.Current!.Title);
    }

    [Fact]
    public async Task A_filter_does_not_leak_into_the_queues_built_from_a_missing_rating()
    {
        var sheets = Sheet(
            Album(1, "", "Unrated", "A", "1970"),
            Album(2, "👍", "Liked", "B", "1971"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.NextUp, shuffle: false, ratingFilter: "👍");

        Assert.Null(session.RatingFilter);
        Assert.Equal("Unrated", session.Current!.Title);
    }

    [Fact]
    public async Task Changing_a_rating_overwrites_the_one_on_the_sheet()
    {
        var sheets = Sheet(Album(1, "👍", "Liked", "A", "1970"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false);
        await session.RateCurrentAsync("❌");

        Assert.Equal("❌", sheets.Cell(Albums, "B3"));
    }

    [Fact]
    public async Task Focusing_reaches_an_album_the_queue_does_not_hold()
    {
        var sheets = Sheet(
            Album(1, "", "Unrated", "A", "1970"),
            Album(2, "⭐", "Starred", "B", "1971"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.NextUp, shuffle: false); // holds only "Unrated"

        Assert.True(session.FocusOn(4)); // "Starred" — sheet row 4
        Assert.Equal("Starred", session.Current!.Title);

        // …and the queue it interrupted carries on afterwards.
        session.Skip();
        Assert.Equal("Unrated", session.Current!.Title);
    }

    [Fact]
    public async Task Focusing_on_a_row_that_is_not_an_album_changes_nothing()
    {
        var sheets = Sheet(Album(1, "", "Unrated", "A", "1970"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.NextUp, shuffle: false);

        Assert.False(session.FocusOn(99));
        Assert.Equal("Unrated", session.Current!.Title);
    }

    [Fact]
    public async Task Starring_an_album_puts_it_on_must_hear()
    {
        var sheets = Sheet(Album(1, "👍", "Liked", "A", "1970"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false);
        await session.RateCurrentAsync("⭐");

        var mustHear = sheets.Rows(MustHear);
        Assert.Equal(2, mustHear.Count); // header + the album
        Assert.Equal(new[] { "1", "Liked", "A", "1970" }, mustHear[1]);
    }

    [Fact]
    public async Task Dropping_a_star_takes_the_album_back_off_must_hear()
    {
        var sheets = Sheet(
            Album(1, "⭐", "Starred", "A", "1970"),
            Album(2, "⭐", "Also starred", "B", "1980"));
        sheets.Add(MustHear, new List<string[]>
        {
            new[] { "#", "Album", "Artist", "Year" },
            new[] { "1", "Starred", "A", "1970" },
            new[] { "2", "Also starred", "B", "1980" },
        });

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false);
        var result = await session.RateCurrentAsync("👎");

        var mustHear = sheets.Rows(MustHear);
        Assert.Equal(2, mustHear.Count); // header + the one that kept its star
        Assert.Equal(new[] { "1", "Also starred", "B", "1980" }, mustHear[1]); // renumbered, no gap
        Assert.Contains("removed", result.MustHearNote);
    }

    [Fact]
    public async Task A_star_that_stays_a_star_is_not_listed_twice()
    {
        var sheets = Sheet(Album(1, "⭐", "Starred", "A", "1970"));
        sheets.Add(MustHear, new List<string[]>
        {
            new[] { "#", "Album", "Artist", "Year" },
            new[] { "1", "Starred", "A", "1970" },
        });

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false);
        await session.RateCurrentAsync("⭐");

        Assert.Equal(2, sheets.Rows(MustHear).Count); // header + the one entry
    }

    [Fact]
    public async Task Re_rating_a_never_starred_album_leaves_must_hear_alone()
    {
        var sheets = Sheet(Album(1, "👍", "Liked", "A", "1970"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false);
        var result = await session.RateCurrentAsync("👎");

        Assert.Null(result.MustHearNote);
        Assert.Single(sheets.Rows(MustHear)); // the header, and nothing else
    }

    [Fact]
    public async Task Stepping_back_onto_a_rated_album_shows_the_rating_that_was_just_saved()
    {
        var sheets = Sheet(
            Album(1, "👍", "Liked", "A", "1970"),
            Album(2, "👍", "Also liked", "B", "1971"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false);
        await session.RateCurrentAsync("❌");
        session.Back();

        Assert.Equal("❌", session.Current!.Rating);
    }

    [Fact]
    public async Task A_saved_rating_reaches_lists_built_from_an_older_copy()
    {
        var sheets = Sheet(Album(1, "👍", "Vs.", "Pearl Jam", "1993"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false);
        await session.RateCurrentAsync("⭐");

        // A row as the phone's baked-in snapshot has it: the rating from whenever it was built,
        // and punctuation that needn't match the sheet's character for character.
        var stale = new ViewRow("1", "👍", "Vs", "The Pearl Jam", "1993");
        RatingChanges.ApplyTo(new[] { stale });

        Assert.Equal("⭐", stale.Rating);
    }

    [Fact]
    public async Task An_album_nobody_rated_this_run_is_left_alone()
    {
        var sheets = Sheet(Album(1, "👍", "Vs.", "Pearl Jam", "1993"));

        var session = await OpenAsync(sheets);
        session.Rebuild(RatingMode.Revisit, shuffle: false);
        await session.RateCurrentAsync("⭐");

        var other = new ViewRow("2", "", "Ten", "Pearl Jam", "1991");
        RatingChanges.ApplyTo(new[] { other });

        Assert.Equal("", other.Rating);
    }

    [Fact]
    public async Task Finding_an_album_narrows_on_every_term()
    {
        var sheets = Sheet(
            Album(1, "", "Vs.", "Pearl Jam", "1993"),
            Album(2, "", "Ten", "Pearl Jam", "1991"));

        var session = await OpenAsync(sheets);

        Assert.Equal("Vs.", Assert.Single(session.Search("pearl 1993")).Title);
        Assert.Equal(2, session.Search("pearl jam").Count);
        Assert.Empty(session.Search("   ")); // a blank query is not "everything"
    }
}
