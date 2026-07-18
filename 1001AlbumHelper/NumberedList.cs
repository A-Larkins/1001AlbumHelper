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

    /// <param name="Rows">Rows carrying a position number, in sheet order.</param>
    /// <param name="Unnumbered">
    /// Rows with content but no number — typically albums typed straight onto the end of the tab.
    /// They're kept separate so a sync can place them properly rather than silently ignoring them.
    /// </param>
    public sealed record Contents(
        int HeaderRow, IReadOnlyList<Row> Rows, IReadOnlyList<Row> Unnumbered);

    /// <summary>True when the numbered rows don't read 1, 2, 3… without gaps.</summary>
    public static bool NumberingIsBroken(Contents contents) =>
        contents.Rows.Where((r, i) => r.Number != (i + 1).ToString()).Any();

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
        var unnumbered = new List<Row>();
        for (int i = header + 1; i < raw.Count; i++)
        {
            var r = raw[i];
            string Cell(int c) => c < r.Count ? r[c].Trim() : "";

            var row = new Row(i + 1, Cell(0), Cell(1), Cell(2), Cell(3));
            if (int.TryParse(Cell(0), out _)) rows.Add(row);
            // An album typed in without a number still counts — but a wholly blank row doesn't.
            else if (row.Title.Length > 0 || row.Artist.Length > 0) unnumbered.Add(row);
        }

        return new Contents(header + 1, rows, unnumbered);
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

    /// <summary>What a <see cref="RepairAsync"/> pass changed.</summary>
    public sealed record Repair(int Placed, bool Renumbered, IReadOnlyList<string> PlacedTitles);

    /// <summary>
    /// Slots any unnumbered rows into their year position and renumbers the list from 1.
    /// <para>
    /// Already-numbered rows keep their existing relative order — only the numbers change. That
    /// matters because the list is year-ordered but not perfectly sorted, so a blanket re-sort
    /// would shuffle rows the owner deliberately placed.
    /// </para>
    /// </summary>
    public static async Task<Repair> RepairAsync(GoogleSheetsWriter writer, string tab)
    {
        var contents = await ReadAsync(writer, tab);

        var ordered = contents.Rows.ToList();
        var placed = new List<string>();

        foreach (var loose in contents.Unnumbered)
        {
            int at = ordered.Count;
            if (int.TryParse(loose.Year, out int year))
            {
                // Same end-of-year-block rule the add path uses.
                at = 0;
                for (int i = 0; i < ordered.Count; i++)
                    if (int.TryParse(ordered[i].Year, out int y) && y <= year) at = i + 1;
            }
            ordered.Insert(at, loose);
            placed.Add(loose.Title.Length > 0 ? loose.Title : loose.Artist);
        }

        bool needsRenumber = NumberingIsBroken(contents) || contents.Unnumbered.Count > 0;
        if (!needsRenumber) return new Repair(0, false, Array.Empty<string>());

        var values = ordered
            .Select((r, i) => (IList<object>)new List<object>
            {
                (i + 1).ToString(), r.Title, r.Artist, r.Year
            })
            .ToList();

        await writer.WriteRangeAsync(tab, $"A{contents.HeaderRow + 1}", values);

        // Placing loose rows compacts the list upward, leaving stragglers at the bottom.
        int lastWritten = contents.HeaderRow + values.Count;
        int lastRead = Math.Max(
            contents.Rows.Count > 0 ? contents.Rows[^1].SheetRow : 0,
            contents.Unnumbered.Count > 0 ? contents.Unnumbered[^1].SheetRow : 0);
        if (lastRead > lastWritten)
            await writer.ClearRangeAsync(tab, $"A{lastWritten + 1}:D{lastRead}");

        return new Repair(placed.Count, true, placed);
    }

    /// <summary>True if an album with the same title and artist is already on the tab.</summary>
    public static bool Contains(Contents contents, string title, string artist) =>
        Find(contents, title, artist) is not null;

    /// <summary>The matching row, or null. Matching is loose — see <see cref="Normalize"/>.</summary>
    public static Row? Find(Contents contents, string title, string artist) =>
        contents.Rows.FirstOrDefault(r =>
            Matches(r.Title, title) && Matches(r.Artist, artist));

    /// <summary>Rows whose title matches but whose artist doesn't — likely-but-unconfirmed dupes.</summary>
    public static IReadOnlyList<Row> FindByTitleOnly(Contents contents, string title, string artist) =>
        contents.Rows.Where(r => Matches(r.Title, title) && !Matches(r.Artist, artist)).ToList();

    public static bool Matches(string a, string b) => Normalize(a) == Normalize(b);

    /// <summary>
    /// Flattens a title or artist for comparison: case, punctuation, accents, a leading "the"
    /// and runs of whitespace all stop mattering. Without this, "The Beatles" and "Beatles!"
    /// read as different artists and duplicates slip through.
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var decomposed = value.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        bool lastWasSpace = false;

        foreach (char c in decomposed)
        {
            // Drop the accent marks that decomposition split off.
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;

            // Apostrophes are elided rather than treated as separators, so "Pepper's" collapses
            // to "peppers" instead of splitting into "pepper s".
            if (c is '\'' or '’' or 'ʼ' or '`') continue;

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (!lastWasSpace && sb.Length > 0)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        string s = sb.ToString().Trim();
        if (s.StartsWith("the ", StringComparison.Ordinal)) s = s[4..];
        return s;
    }
}
