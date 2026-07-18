namespace _1001AlbumHelper;

/// <summary>
/// Insert-by-year support for the two hand-numbered tabs — "Must Hear" and the replacements list.
/// <para>
/// Both tabs share the same physical layout: <c>A</c> = position number, <c>B</c> = album title,
/// <c>C</c> = artist, <c>D</c> = year. (The replacements tab's header row labels B/C as
/// "Artist,Album", but its actual data is album-then-artist like Must Hear, so the layout below
/// follows the data rather than the mislabeled header.)
/// </para>
/// </summary>
public static class NumberedList
{
    public sealed record Row(int SheetRow, string Number, string Title, string Artist, string Year);

    public sealed record Contents(int HeaderRow, IReadOnlyList<Row> Rows);

    /// <summary>Reads a numbered tab, skipping anything above the "#" header.</summary>
    public static async Task<Contents> ReadAsync(GoogleSheetsWriter writer, string tab)
    {
        var raw = await writer.ReadTabAsync(tab, "A1:D");

        int header = -1;
        for (int i = 0; i < raw.Count; i++)
            if (raw[i].Count > 0 && raw[i][0].Trim() == "#") { header = i; break; }

        if (header < 0)
            throw new InvalidOperationException(
                $"Couldn't find the header row (a cell reading \"#\") on tab \"{tab}\".");

        var rows = new List<Row>();
        for (int i = header + 1; i < raw.Count; i++)
        {
            var r = raw[i];
            string Cell(int c) => c < r.Count ? r[c].Trim() : "";

            if (!int.TryParse(Cell(0), out _)) continue;
            rows.Add(new Row(i + 1, Cell(0), Cell(1), Cell(2), Cell(3)));
        }

        return new Contents(header + 1, rows);
    }

    /// <summary>
    /// Works out where an album of <paramref name="year"/> belongs: the sheet row to insert at,
    /// and the position number it will take.
    /// <para>
    /// Walks to the last row whose year is &lt;= the new one. On a year-ordered list that lands at
    /// the end of the matching year's block, or at the end of the closest earlier year when that
    /// year isn't represented yet. Rows with an unreadable year are stepped over.
    /// </para>
    /// </summary>
    public static (int SheetRow, int Position) PlaceByYear(Contents contents, int year)
    {
        int insertAfterSheetRow = contents.HeaderRow;
        foreach (var row in contents.Rows)
        {
            if (int.TryParse(row.Year, out int y) && y <= year)
                insertAfterSheetRow = row.SheetRow;
        }

        int newSheetRow = insertAfterSheetRow + 1;
        int position = contents.Rows.Count(r => r.SheetRow < newSheetRow) + 1;
        return (newSheetRow, position);
    }

    /// <summary>
    /// Inserts an album at the end of its year block and renumbers the list from 1.
    /// Returns the position it landed at.
    /// </summary>
    public static async Task<int> InsertByYearAsync(
        GoogleSheetsWriter writer, string tab, string title, string artist, int year)
    {
        var contents = await ReadAsync(writer, tab);
        var (newSheetRow, position) = PlaceByYear(contents, year);

        // Copy formatting from a neighbouring *data* row so the new row matches the list. Prefer
        // the row above, unless we're inserting at the very top — in which case the row above is
        // the header, and we take the row below (which the insert shifts down by one) instead.
        int? formatSource = newSheetRow > contents.HeaderRow + 1
            ? newSheetRow - 1
            : contents.Rows.Count > 0 ? newSheetRow + 1 : null;

        await writer.InsertRowAsync(tab, newSheetRow, new List<object>
        {
            position.ToString(), title, artist, year.ToString()
        }, formatSource);

        // Everything below shifted down by one, so renumber the whole list in one write.
        int total = contents.Rows.Count + 1;
        var numbers = Enumerable.Range(1, total).Select(n => n.ToString()).ToList();
        await writer.WriteColumnAsync(tab, "A", contents.HeaderRow + 1, numbers);

        return position;
    }

    /// <summary>True if an album with the same title and artist is already on the tab.</summary>
    public static bool Contains(Contents contents, string title, string artist) =>
        contents.Rows.Any(r =>
            string.Equals(r.Title, title, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Artist, artist, StringComparison.OrdinalIgnoreCase));
}
