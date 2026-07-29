namespace _1001AlbumHelper;

/// <summary>
/// The sheet operations <see cref="NumberedList"/> and <see cref="RatingSession"/> need, factored
/// out of <see cref="GoogleSheetsWriter"/> so they can run against either it (desktop, OAuth via
/// Google.Apis) or a REST-based client (mobile, service account — Google.Apis can't run on iOS; see
/// <see cref="CandidateSheet"/> for why). Both back the same live spreadsheet; only the transport differs.
/// </summary>
public interface ISheetsClient
{
    /// <summary>Reads a tab's cells as raw strings. Short rows are returned as-is. Read-only.</summary>
    Task<IReadOnlyList<IReadOnlyList<string>>> ReadTabAsync(string tabName, string a1Range);

    /// <summary>Writes a single cell, leaving every other cell — and all formatting — untouched.</summary>
    Task UpdateCellAsync(string tabName, string cellA1, string value);

    /// <summary>
    /// Inserts a blank row above <paramref name="rowNumber"/> (1-indexed) and fills it in, shifting
    /// everything below down. <paramref name="formatSourceRow"/>, numbered as it is *after* the
    /// insert, is an existing data row whose formatting the new row should copy.
    /// </summary>
    Task InsertRowAsync(string tabName, int rowNumber, IList<object> values, int? formatSourceRow = null);

    /// <summary>Overwrites a rectangular block of values starting at <paramref name="topLeft"/> (e.g. "A2"). Values only.</summary>
    Task WriteRangeAsync(string tabName, string topLeft, IList<IList<object>> rows);

    /// <summary>Blanks a range's values.</summary>
    Task ClearRangeAsync(string tabName, string a1Range);

    /// <summary>Overwrites a single column from <paramref name="startRow"/> down. Other columns are untouched.</summary>
    Task WriteColumnAsync(string tabName, string column, int startRow, IReadOnlyList<string> values);
}
